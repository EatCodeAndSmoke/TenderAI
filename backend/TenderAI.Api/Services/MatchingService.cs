using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class MatchingService(
        TenderAiDbContext db,
        ITenderMatchEvaluator evaluator,
        TimeProvider timeProvider)
    {
        private static readonly TimeSpan TashkentOffset = TimeSpan.FromHours(5);
        private const int MaxConcurrentEvaluations = 2;

        public async Task<IReadOnlyList<MatchDto>> RunAsync(CancellationToken ct)
        {
            var now = timeProvider.GetUtcNow().ToOffset(TashkentOffset).DateTime;
            var today = DateOnly.FromDateTime(now);
            var paymentCutoff = today.AddMonths(-12);

            var customers = await db.Customers
                .AsNoTracking()
                .Include(customer => customer.RiskFeatures)
                .Include(customer => customer.Payments)
                .AsSplitQuery()
                .OrderBy(customer => customer.CustomerId)
                .ToListAsync(ct);

            var tenders = await db.Tenders
                .AsNoTracking()
                .Include(tender => tender.Products)
                .Where(tender => tender.EndDate >= now)
                .OrderBy(tender => tender.EndDate)
                .ThenBy(tender => tender.Id)
                .ToListAsync(ct);

            using var semaphore = new SemaphoreSlim(MaxConcurrentEvaluations);
            var tasks = new List<Task<NotificationEntity?>>();
            foreach (var customer in customers)
            {
                foreach (var tender in tenders)
                {
                    tasks.Add(EvaluatePairAsync(customer, tender));
                }
            }

            var notifications = (await Task.WhenAll(tasks))
                .OfType<NotificationEntity>()
                .ToList();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var namesById = customers.ToDictionary(customer => customer.CustomerId, customer => customer.Name);
            return notifications
                .OrderByDescending(notification => notification.CreatedAt)
                .ThenBy(notification => notification.CustomerId)
                .ThenBy(notification => notification.LotId)
                .Select(notification => notification.ToMatchDto(namesById[notification.CustomerId]))
                .ToList();

            async Task<NotificationEntity?> EvaluatePairAsync(CustomerEntity customer, TenderEntity tender)
            {
                var industryCode = customer.IndustryCode.Trim();
                var industryName = customer.IndustryName.Trim();
                var products = tender.Products
                    .Where(product => !string.IsNullOrWhiteSpace(product.ProductName))
                    .Select(product => new TenderProductEvidence(
                        product.ProductCode,
                        product.CategoryName,
                        product.ProductName,
                        product.Description,
                        product.TechnicalSpecifications))
                    .ToList();
                var payments = customer.Payments
                    .Where(payment => payment.IsIncoming
                        && payment.Date >= paymentCutoff
                        && payment.Date <= today
                        && !string.IsNullOrWhiteSpace(payment.PurposeDescription))
                    .OrderByDescending(payment => payment.Date)
                    .Take(30)
                    .Select(payment => new IncomingPaymentEvidence(payment.Date, payment.PurposeDescription))
                    .ToList();

                if (industryCode.Length == 0 || industryName.Length == 0 || products.Count == 0 || payments.Count == 0)
                {
                    return null;
                }

                await semaphore.WaitAsync(ct);
                TenderMatchEvaluation assessment;
                try
                {
                    assessment = await evaluator.EvaluateAsync(industryCode, industryName, products, payments, ct);
                }
                finally
                {
                    semaphore.Release();
                }

                if (!assessment.OkedSupportsProducts || !assessment.IncomingPaymentsSupportProducts)
                {
                    return null;
                }

                var (offer, loanReason) = GetLoanDecision(customer, tender);
                return new NotificationEntity
                {
                    Id = $"N-{Guid.NewGuid():N}"[..20],
                    CustomerId = customer.CustomerId,
                    LotId = tender.Id.ToString(CultureInfo.InvariantCulture),
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                    Title = tender.Name,
                    Body = GetTenderBody(tender),
                    Buyer = tender.BuyerName,
                    SourceUrl = tender.SourceUrl,
                    Region = tender.DeliveryRegion ?? tender.RegionName ?? "",
                    StartPrice = tender.StartPrice,
                    CurrencyCode = tender.CurrencyCode,
                    Deadline = DateOnly.FromDateTime(tender.EndDate),
                    PaymentTermsDays = tender.PaymentTermDays ?? 0,
                    Requirements = GetRequirements(tender),
                    MatchReason = assessment.Reason,
                    LoanDecisionReason = loanReason,
                    Offer = offer
                };
            }
        }

        private static (LoanOfferEntity? Offer, string Reason) GetLoanDecision(
            CustomerEntity customer,
            TenderEntity tender)
        {
            var limit = customer.RiskFeatures?.PreApprovedLimit;
            if (limit is not > 0)
            {
                return (null, "No pre-approved limit is on record, so a loan application is not suggested.");
            }

            if (!string.Equals(tender.CurrencyCode, "UZS", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "A pre-approved limit is on record, but this tender is not in UZS; no application is suggested.");
            }

            var financing = ToAmount(Math.Min(tender.StartPrice, limit.Value));
            var guarantee = 0L;
            if (tender.BankGuaranteeAccepted && tender.DepositPercent is > 0)
            {
                var requestedGuarantee = ToAmount(tender.StartPrice * tender.DepositPercent.Value / 100m);
                if (requestedGuarantee <= limit.Value)
                {
                    guarantee = requestedGuarantee;
                }
            }

            if (financing == 0 && guarantee == 0)
            {
                return (null, "A pre-approved limit is on record, but no eligible UZS application amount is available for this tender.");
            }

            var offer = new LoanOfferEntity
            {
                Financing = financing,
                Guarantee = guarantee,
                PreApprovedLimit = limit.Value
            };
            var products = new List<string>();
            if (financing > 0)
            {
                products.Add($"financing up to {FormatAmount(financing)} so'm");
            }

            if (guarantee > 0)
            {
                products.Add($"a bid guarantee of {FormatAmount(guarantee)} so'm");
            }

            return (
                offer,
                $"Pre-approved limit present; you may apply for {string.Join(" and ", products)}. Final approval requires bank review.");
        }

        private static long ToAmount(decimal amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            return decimal.ToInt64(decimal.Truncate(Math.Min(amount, long.MaxValue)));
        }

        private static string FormatAmount(long amount) =>
            amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

        private static string GetTenderBody(TenderEntity tender)
        {
            if (!string.IsNullOrWhiteSpace(tender.Summary))
            {
                return tender.Summary;
            }

            if (!string.IsNullOrWhiteSpace(tender.Description))
            {
                return tender.Description;
            }

            if (!string.IsNullOrWhiteSpace(tender.DetailedName))
            {
                return tender.DetailedName;
            }

            return tender.Name;
        }

        private static List<string> GetRequirements(TenderEntity tender) =>
            tender.Products.Select(product => product.ProductName)
                .Concat(tender.BuyerRequirements)
                .Concat(tender.Requirements)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
    }
}
