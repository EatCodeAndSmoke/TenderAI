using System.Globalization;
using System.Runtime.CompilerServices;
using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class MatchingService(
        TenderAiDbContext db,
        ITenderMatchEvaluator evaluator,
        TimeProvider timeProvider,
        ILogger<MatchingService> logger)
    {
        private static readonly TimeSpan TashkentOffset = TimeSpan.FromHours(5);
        private const int MaxConcurrentCustomerEvaluations = 2;

        public async IAsyncEnumerable<MatchRunEvent> RunStreamingAsync(
            [EnumeratorCancellation] CancellationToken ct)
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

            var storedTenders = await db.Tenders
                .AsNoTracking()
                .Include(tender => tender.Products)
                .Where(tender => tender.EndDate >= now)
                .OrderBy(tender => tender.EndDate)
                .ThenBy(tender => tender.Id)
                .ToListAsync(ct);
            var tenders = storedTenders.Select(ToTenderEvidence).ToList();
            var tendersById = storedTenders.ToDictionary(
                tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

            yield return new MatchRunEvent("started", TotalCustomers: customers.Count, TotalTenders: tenders.Count);

            using var semaphore = new SemaphoreSlim(MaxConcurrentCustomerEvaluations);
            var pending = customers
                .Select(customer => EvaluateCustomerAsync(customer, tenders, paymentCutoff, today, semaphore, ct))
                .ToHashSet();
            var completedCount = 0;
            var failedCount = 0;

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var finishedTask = await Task.WhenAny(pending);
                pending.Remove(finishedTask);
                var evaluation = await finishedTask;
                completedCount++;

                if (evaluation.Error is not null)
                {
                    failedCount++;
                    yield return new MatchRunEvent(
                        "customer-error",
                        TotalCustomers: customers.Count,
                        TotalTenders: tenders.Count,
                        CompletedCustomers: completedCount,
                        FailedCustomers: failedCount,
                        CustomerId: evaluation.Customer.CustomerId,
                        CustomerName: evaluation.Customer.Name,
                        Error: DescribeError(evaluation.Error));
                    continue;
                }

                var timestamp = timeProvider.GetUtcNow().UtcDateTime;
                var notifications = evaluation.Matches!
                    .Select(match => CreateNotification(
                        evaluation.Customer,
                        tendersById[match.TenderId],
                        match.Reason,
                        timestamp))
                    .ToList();

                await using (var transaction = await db.Database.BeginTransactionAsync(ct))
                {
                    await db.Notifications
                        .Where(notification => notification.CustomerId == evaluation.Customer.CustomerId)
                        .ExecuteDeleteAsync(ct);
                    db.Notifications.AddRange(notifications);
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }

                var matchDtos = notifications
                    .Select(notification => notification.ToMatchDto(evaluation.Customer.Name))
                    .ToList();
                yield return new MatchRunEvent(
                    "customer-completed",
                    TotalCustomers: customers.Count,
                    TotalTenders: tenders.Count,
                    CompletedCustomers: completedCount,
                    FailedCustomers: failedCount,
                    CustomerId: evaluation.Customer.CustomerId,
                    CustomerName: evaluation.Customer.Name,
                    Matches: matchDtos);
            }

            yield return new MatchRunEvent(
                "completed",
                TotalCustomers: customers.Count,
                TotalTenders: tenders.Count,
                CompletedCustomers: completedCount,
                FailedCustomers: failedCount);
        }

        private async Task<CustomerEvaluationResult> EvaluateCustomerAsync(
            CustomerEntity customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders,
            DateOnly paymentCutoff,
            DateOnly today,
            SemaphoreSlim semaphore,
            CancellationToken ct)
        {
            var acquired = false;
            try
            {
                await semaphore.WaitAsync(ct);
                acquired = true;
                var industryCode = customer.IndustryCode.Trim();
                var industryName = customer.IndustryName.Trim();
                var payments = customer.Payments
                    .Where(payment => payment.IsIncoming
                        && payment.Date >= paymentCutoff
                        && payment.Date <= today
                        && !string.IsNullOrWhiteSpace(payment.PurposeDescription))
                    .OrderByDescending(payment => payment.Date)
                    .Take(30)
                    .Select(payment => new IncomingPaymentEvidence(payment.Date, payment.PurposeDescription))
                    .ToList();
                var evidence = new CustomerMatchEvidence(
                    customer.CustomerId,
                    industryCode,
                    industryName,
                    customer.RegionName,
                    customer.DistrictName,
                    payments);
                var matches = await evaluator.EvaluateCustomerAsync(evidence, tenders, ct);
                return new CustomerEvaluationResult(customer, ValidateMatches(matches, tenders));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tender matching failed for customer {CustomerId}.", customer.CustomerId);
                return new CustomerEvaluationResult(customer, Error: ex);
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }
            }
        }

        private static IReadOnlyList<TenderCandidateMatch> ValidateMatches(
            IReadOnlyList<TenderCandidateMatch> matches,
            IReadOnlyCollection<TenderMatchEvidence> tenders)
        {
            var knownTenderIds = tenders.Select(tender => tender.TenderId).ToHashSet(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                if (!knownTenderIds.Contains(match.TenderId)
                    || !ids.Add(match.TenderId)
                    || string.IsNullOrWhiteSpace(match.Reason)
                    || match.Reason.Length > 240
                    || match.Reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 25)
                {
                    throw new TenderMatchEvaluationException("The matching response contained invalid tender results.");
                }
            }

            return matches;
        }

        private NotificationEntity CreateNotification(
            CustomerEntity customer,
            TenderEntity tender,
            string matchReason,
            DateTime timestamp)
        {
            var (offer, loanReason) = GetLoanDecision(customer, tender);
            return new NotificationEntity
            {
                Id = $"N-{Guid.NewGuid():N}"[..20],
                CustomerId = customer.CustomerId,
                LotId = tender.Id.ToString(CultureInfo.InvariantCulture),
                CreatedAt = timestamp,
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
                MatchReason = matchReason,
                LoanDecisionReason = loanReason,
                Offer = offer
            };
        }

        private static (LoanOfferEntity? Offer, string Reason) GetLoanDecision(
            CustomerEntity customer,
            TenderEntity tender)
        {
            var risk = customer.RiskFeatures;
            var limit = risk?.PreApprovedLimit;
            if (limit is not > 0)
            {
                return (null, "No pre-approved limit is on record, so a loan application is not suggested.");
            }

            if (!string.Equals(tender.CurrencyCode, "UZS", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "This tender is not in UZS, so a loan application is not suggested.");
            }

            if (limit.Value < tender.StartPrice * 0.8m)
            {
                return (null, "The pre-approved limit is below 80% of the tender amount.");
            }

            if (risk!.OverdueAmount > 0 || risk.MaxOverdueDays12m > 0)
            {
                return (null, "Payment arrears are recorded in the last 12 months, so a loan application is not suggested.");
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

            if (financing == 0)
            {
                return (null, "No eligible application amount is available for this tender.");
            }

            var products = new List<string> { $"financing up to {FormatAmount(financing)} so'm" };
            if (guarantee > 0)
            {
                products.Add($"a bid guarantee of {FormatAmount(guarantee)} so'm");
            }

            return (
                new LoanOfferEntity
                {
                    Financing = financing,
                    Guarantee = guarantee,
                    PreApprovedLimit = limit.Value
                },
                $"Limit covers at least 80% of the tender and no arrears are recorded; you may apply for {string.Join(" and ", products)}. Final approval requires bank review.");
        }

        private static TenderMatchEvidence ToTenderEvidence(TenderEntity tender)
        {
            var products = tender.Products
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductName))
                .OrderBy(product => product.OrderNum)
                .Select(product => new TenderProductEvidence(
                    product.ProductCode,
                    product.CategoryName,
                    product.ProductName,
                    product.Description,
                    product.TechnicalSpecifications))
                .ToList();
            var description = string.Join(
                "\n",
                new[] { tender.Summary, tender.Name, tender.DetailedName, tender.Description, tender.AdditionalDescription }
                    .Concat(products.SelectMany(product => new[]
                    {
                        product.ProductName,
                        product.CategoryName,
                        product.Description,
                        product.TechnicalSpecifications
                    }))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return new TenderMatchEvidence(
                tender.Id.ToString(CultureInfo.InvariantCulture),
                description,
                tender.DeliveryRegion ?? tender.RegionName,
                tender.DeliveryDistrict ?? tender.DistrictName,
                products);
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

        private static string DescribeError(Exception exception)
        {
            if (exception is AmazonServiceException { ErrorCode: { } errorCode })
            {
                return errorCode.Equals("ThrottlingException", StringComparison.OrdinalIgnoreCase)
                    || errorCode.Equals("TooManyRequestsException", StringComparison.OrdinalIgnoreCase)
                    ? "Bedrock throttled this customer request after retries. Other customers continue; retry this run later."
                    : $"Bedrock request failed ({errorCode}). Other customers continue.";
            }

            if (exception is TenderMatchEvaluationException)
            {
                return "Bedrock returned an invalid matching response. Other customers continue.";
            }

            return "This customer could not be evaluated. Other customers continue.";
        }

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

        private sealed record CustomerEvaluationResult(
            CustomerEntity Customer,
            IReadOnlyList<TenderCandidateMatch>? Matches = null,
            Exception? Error = null);
    }

    public sealed record MatchRunEvent(
        string Type,
        int TotalCustomers = 0,
        int TotalTenders = 0,
        int CompletedCustomers = 0,
        int FailedCustomers = 0,
        string? CustomerId = null,
        string? CustomerName = null,
        IReadOnlyList<MatchDto>? Matches = null,
        string? Error = null);

}
