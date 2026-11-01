using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class CustomerGenerationService(TenderAiDbContext db)
    {
        private static readonly string[] CompanyNames = ["Orzu", "Navqiron", "Zarafshan", "Istiqlol", "Yangi Avlod", "Taraqqiyot"];
        private static readonly string[] CompanyTypes = ["Trade", "Supplies", "Enterprise"];
        private static readonly string[] Regions =
        [
            "Tashkent", "Tashkent region", "Andijan", "Bukhara", "Fergana", "Jizzakh", "Karakalpakstan",
            "Kashkadarya", "Khorezm", "Namangan", "Navoi", "Samarkand", "Surkhandarya", "Syrdarya"
        ];
        private static readonly string[] Districts = ["Yunusobod", "Chilonzor", "Mirzo Ulugbek", "Sergeli", "Yakkasaray"];
        private static readonly (Regex Pattern, string Code, string Name)[] Industries =
        [
            (new(@"\b(bread|bakery|bakeries|flour|buns?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "10710", "Manufacture of bread and bakery products"),
            (new(@"\b(food|foods|grocery|groceries|rice|meat|vegetable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46390", "Wholesale of food products"),
            (new(@"\b(paper|stationery|office supplies)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46499", "Wholesale of paper and stationery"),
            (new(@"\b(cleaning|janitorial)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "81210", "Cleaning services"),
            (new(@"\b(roof|repair|construction|renovation)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "41000", "Building construction and repair"),
            (new(@"\b(medicine|medical|pharmaceutical)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46460", "Wholesale of pharmaceutical goods"),
            (new(@"\b(computer|laptop|hardware)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46510", "Wholesale of computers and equipment"),
            (new(@"\b(software|information technology)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "62010", "Computer programming activities")
        ];

        public async Task<IReadOnlyList<CustomerRecordDto>> GenerateAsync(CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var tenders = await db.Tenders
                .AsNoTracking()
                .Where(t => t.Summary != null && t.Summary != "")
                .Select(t => new TenderSource(t.Summary!, t.StartPrice, t.RegionName, t.DeliveryRegion, t.DeliveryDistrict))
                .ToListAsync(ct);
            tenders = tenders.Where(t => !string.IsNullOrWhiteSpace(t.Summary)).ToList();
            if (tenders.Count == 0)
            {
                throw new NoAvailableTenderSummaryException();
            }

            var existing = await db.Customers
                .AsNoTracking()
                .Select(c => new { c.CustomerId, c.TaxCode })
                .ToListAsync(ct);
            var nextNumber = existing.Select(c => CustomerNumber(c.CustomerId)).DefaultIfEmpty(0).Max() + 1;
            var taxCodes = new HashSet<string>(existing.Select(c => c.TaxCode), StringComparer.Ordinal);
            var identities = Enumerable.Range(0, 3)
                .Select(index => new CustomerIdentity(
                    $"C-{nextNumber + index:D2}",
                    NextTaxCode(taxCodes)))
                .ToArray();
            var plans = new[]
            {
                new GenerationPlan("en", MatchesTender: true, LoanEligible: true),
                new GenerationPlan("ru", MatchesTender: true, LoanEligible: false),
                new GenerationPlan("uz", MatchesTender: false, LoanEligible: false)
            };
            var today = DateOnly.FromDateTime(DateTime.Today);

            var generationTasks = plans.Select((plan, index) =>
            {
                var tender = tenders[Random.Shared.Next(tenders.Count)];
                return Task.Run(() => CreateCustomer(identities[index], plan, tender, today), ct);
            });
            var generated = await Task.WhenAll(generationTasks);

            db.Customers.AddRange(generated);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return generated.Select(customer => customer.ToDto(today)).ToList();
        }

        private static CustomerEntity CreateCustomer(
            CustomerIdentity identity,
            GenerationPlan plan,
            TenderSource tender,
            DateOnly today)
        {
            var subject = TenderSubject(tender.Summary);
            var industry = IndustryFor(tender.Summary, subject);
            var monthsWithBank = plan.LoanEligible ? Random.Shared.Next(24, 61) : plan.MatchesTender ? 18 : 6;
            var customerSince = today.AddMonths(-monthsWithBank);
            var tenderMonthlyInflow = CeilingToLong(tender.StartPrice / 3m);
            var monthlyInflow = plan.MatchesTender
                ? Math.Max(80_000_000L, tenderMonthlyInflow)
                : Random.Shared.Next(20, 71) * 1_000_000L;
            var preApprovedLimit = plan.LoanEligible
                ? Math.Max(Random.Shared.Next(100, 301) * 1_000_000L, CeilingToLong(tender.StartPrice / 2m))
                : plan.MatchesTender ? 150_000_000L : (long?)null;

            return new CustomerEntity
            {
                CustomerId = identity.CustomerId,
                Name = $"{Pick(CompanyNames)} {Pick(CompanyTypes)} {identity.CustomerId} LLC",
                TaxCode = identity.TaxCode,
                Type = "LLC",
                IndustryCode = industry.Code,
                IndustryName = industry.Name,
                RegionName = FirstNonBlank(tender.DeliveryRegion, tender.RegionName) ?? Pick(Regions),
                DistrictName = FirstNonBlank(tender.DeliveryDistrict) ?? Pick(Districts),
                LanguageCode = plan.LanguageCode,
                RegistrationDate = today.AddMonths(-(monthsWithBank + Random.Shared.Next(12, 49))),
                CustomerSince = customerSince,
                OnRegister = !plan.MatchesTender,
                RiskFeatures = new CustomerRiskFeaturesEntity
                {
                    CustomerId = identity.CustomerId,
                    AvgMonthlyInflow6m = monthlyInflow,
                    OutstandingAmount = 0,
                    OverdueAmount = plan.LoanEligible || !plan.MatchesTender ? 0 : 1_000_000,
                    MaxOverdueDays12m = plan.LoanEligible || !plan.MatchesTender ? 0 : 45,
                    PreApprovedLimit = preApprovedLimit,
                    KycPassed = true
                },
                Payments = plan.MatchesTender
                    ? CreatePayments(identity.CustomerId, subject, today)
                    : []
            };
        }

        private static List<CustomerPaymentEntity> CreatePayments(string customerId, string subject, DateOnly today)
        {
            var offsets = new[] { 7, 37, 68 };
            return offsets.Select((daysAgo, index) => new CustomerPaymentEntity
            {
                CustomerId = customerId,
                Date = today.AddDays(-daysAgo),
                IsIncoming = index != 2,
                PurposeDescription = $"Payment for {subject} under procurement contract",
                Amount = Random.Shared.Next(2, 31) * 1_000_000L
            }).ToList();
        }

        private static (string Code, string Name) IndustryFor(string summary, string subject)
        {
            var industry = Industries.FirstOrDefault(x => x.Pattern.IsMatch(summary));
            return industry.Pattern is not null
                ? (industry.Code, industry.Name)
                : ("46900", $"Specialized supply for {subject}"[..Math.Min(200, $"Specialized supply for {subject}".Length)]);
        }

        private static string TenderSubject(string summary)
        {
            var firstSentence = Regex.Split(summary.Trim(), @"(?<=[.!?])\s+")[0];
            var subject = Regex.Replace(
                firstSentence,
                @"^(?:The buyer is (?:buying|purchasing|seeking)|The tender is for|Supply of|Purchase of|Procurement of)\s+",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            return subject.Length <= 160 ? subject : subject[..160].Trim();
        }

        private static string NextTaxCode(HashSet<string> used)
        {
            string value;
            do
            {
                value = Random.Shared.Next(100_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture);
            } while (!used.Add(value));
            return value;
        }

        private static int CustomerNumber(string customerId)
        {
            var digits = new string(customerId.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static long CeilingToLong(decimal value) =>
            value <= 0 ? 0 : value >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(value);

        private static string Pick(string[] values) => values[Random.Shared.Next(values.Length)];

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private sealed record CustomerIdentity(string CustomerId, string TaxCode);
        private sealed record GenerationPlan(string LanguageCode, bool MatchesTender, bool LoanEligible);
        private sealed record TenderSource(string Summary, decimal StartPrice, string? RegionName, string? DeliveryRegion, string? DeliveryDistrict);
    }

    public sealed class NoAvailableTenderSummaryException()
        : Exception("Load at least one tender with an English summary before generating customers.");
}
