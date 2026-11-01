using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class CustomerGenerationService(TenderAiDbContext db, ICustomerProfileGenerator profileGenerator)
    {
        private static readonly Regex CustomerIdPattern = new(@"\bC[\s_-]*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WordsPattern = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);
        private static readonly (Regex Pattern, string Code)[] IndustryCodes =
        [
            (new(@"\b(bread|bakery|bakeries|flour|buns?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "10710"),
            (new(@"\b(food|foods|grocery|groceries|rice|meat|vegetable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46390"),
            (new(@"\b(paper|stationery|office supplies)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46499"),
            (new(@"\b(cleaning|janitorial)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "81210"),
            (new(@"\b(roof|repair|construction|renovation)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "41000"),
            (new(@"\b(medicine|medical|pharmaceutical)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46460"),
            (new(@"\b(computer|laptop|hardware)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46510"),
            (new(@"\b(software|information technology)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "62010")
        ];

        public async Task<IReadOnlyList<CustomerRecordDto>> GenerateAsync(CancellationToken ct)
        {
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

            var matchingTender = Pick(tenders);
            var unmatchedTender = Pick(tenders);
            var plans = new[]
            {
                new GenerationPlan("en", true, true, matchingTender),
                new GenerationPlan("ru", true, false, matchingTender),
                new GenerationPlan("uz", false, false, unmatchedTender)
            };

            var drafts = await Task.WhenAll(plans.Select(async plan =>
            {
                var draft = await profileGenerator.GenerateAsync(
                    plan.Tender.Summary,
                    plan.LanguageCode,
                    plan.MatchesTender,
                    ct);
                ValidateDraft(draft, plan);
                return draft;
            }));

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.Customers
                .AsNoTracking()
                .Select(c => new { c.CustomerId, c.TaxCode })
                .ToListAsync(ct);
            var nextNumber = existing.Select(c => CustomerNumber(c.CustomerId)).DefaultIfEmpty(0).Max() + 1;
            var taxCodes = new HashSet<string>(existing.Select(c => c.TaxCode), StringComparer.Ordinal);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var customers = plans.Select((plan, index) =>
                CreateCustomer(
                    new CustomerIdentity($"C-{nextNumber + index:D2}", NextTaxCode(taxCodes)),
                    plan,
                    drafts[index],
                    today)).ToList();

            db.Customers.AddRange(customers);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return customers.Select(customer => customer.ToDto(today)).ToList();
        }

        private static CustomerEntity CreateCustomer(
            CustomerIdentity identity,
            GenerationPlan plan,
            CustomerProfileDraft draft,
            DateOnly today)
        {
            var monthsWithBank = plan.MonthsWithBank;
            var customerSince = today.AddMonths(-monthsWithBank);
            var activity = draft.IndustryName!.Trim();
            var companyName = AddLegalForm(draft.CompanyName!.Trim());
            var monthlyInflow = plan.MatchesTender
                ? Math.Max(80_000_000L, CeilingToLong(plan.Tender.StartPrice / 3m))
                : Random.Shared.Next(20, 71) * 1_000_000L;
            var preApprovedLimit = plan.LoanEligible
                ? Math.Max(Random.Shared.Next(100, 301) * 1_000_000L, CeilingToLong(plan.Tender.StartPrice / 2m))
                : plan.MatchesTender ? 150_000_000L : (long?)null;

            return new CustomerEntity
            {
                CustomerId = identity.CustomerId,
                Name = companyName,
                TaxCode = identity.TaxCode,
                Type = "LLC",
                IndustryCode = IndustryCodeFor(activity),
                IndustryName = activity,
                RegionName = FirstNonBlank(plan.Tender.DeliveryRegion, plan.Tender.RegionName) ?? "Tashkent",
                DistrictName = FirstNonBlank(plan.Tender.DeliveryDistrict) ?? "Yunusobod",
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
                    ? draft.RecentPaymentDescriptions!.Select((description, index) => new CustomerPaymentEntity
                    {
                        CustomerId = identity.CustomerId,
                        Date = today.AddDays(new[] { -7, -37, -68 }[index]),
                        IsIncoming = index != 2,
                        PurposeDescription = description.Trim(),
                        Amount = Random.Shared.Next(2, 31) * 1_000_000L
                    }).ToList()
                    : []
            };
        }

        private static void ValidateDraft(CustomerProfileDraft draft, GenerationPlan plan)
        {
            var companyName = draft.CompanyName?.Trim();
            var industryName = draft.IndustryName?.Trim();
            if (string.IsNullOrWhiteSpace(companyName)
                || companyName.Length > 160
                || WordsPattern.Matches(companyName).Count > 10)
            {
                throw InvalidProfile("The generated company name is missing or too long.");
            }

            if (string.IsNullOrWhiteSpace(industryName)
                || industryName.Length > 100
                || WordsPattern.Matches(industryName).Count > 9)
            {
                throw InvalidProfile("The generated industry activity is missing or too long.");
            }

            if (draft.MatchesTender != plan.MatchesTender)
            {
                throw InvalidProfile("The generated profile didn't meet its assigned tender-fit requirement.");
            }

            if (ContainsCustomerId(companyName) || ContainsCustomerId(industryName))
            {
                throw InvalidProfile("Generated display text must not contain a customer ID.");
            }

            if (ContainsLongTenderPhrase(companyName, plan.Tender.Summary)
                || ContainsLongTenderPhrase(industryName, plan.Tender.Summary))
            {
                throw InvalidProfile("Generated display text copied too much wording from the tender summary.");
            }

            var payments = draft.RecentPaymentDescriptions;
            if (plan.MatchesTender)
            {
                if (payments is null || payments.Count != 3)
                {
                    throw InvalidProfile("A matching profile must contain exactly three recent payment descriptions.");
                }

                if (payments.Any(payment => string.IsNullOrWhiteSpace(payment)
                    || payment.Trim().Length > 120
                    || WordsPattern.Matches(payment).Count > 14))
                {
                    throw InvalidProfile("A generated payment description is missing or too long.");
                }

                if (payments.Select(payment => payment.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3
                    || !ArePaymentDescriptionsVaried(payments))
                {
                    throw InvalidProfile("Recent payment descriptions must be distinct and meaningfully varied.");
                }

                if (payments.Any(payment => ContainsCustomerId(payment) || ContainsLongTenderPhrase(payment, plan.Tender.Summary)))
                {
                    throw InvalidProfile("A payment description contains an ID or copied tender wording.");
                }
            }
            else if (payments is { Count: > 0 })
            {
                throw InvalidProfile("The non-matching profile must not contain tender-related payments.");
            }
        }

        private static bool ContainsCustomerId(string value) =>
            CustomerIdPattern.IsMatch(value)
            || Regex.IsMatch(
                value,
                @"\b(customer|client|account)\s*(id|no\.?|number)?\s*[:#-]?\s*\d+\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool ArePaymentDescriptionsVaried(IReadOnlyList<string> descriptions)
        {
            var wordSets = descriptions
                .Select(description => WordsPattern.Matches(description)
                    .Select(match => match.Value.ToLowerInvariant())
                    .ToHashSet(StringComparer.Ordinal))
                .ToArray();

            for (var first = 0; first < wordSets.Length; first++)
            {
                for (var second = first + 1; second < wordSets.Length; second++)
                {
                    var common = wordSets[first].Intersect(wordSets[second], StringComparer.Ordinal).Count();
                    var total = wordSets[first].Union(wordSets[second], StringComparer.Ordinal).Count();
                    if (total == 0 || (double)common / total > 0.6) return false;
                }
            }

            return true;
        }

        private static bool ContainsLongTenderPhrase(string value, string summary)
        {
            var outputWords = WordsPattern.Matches(value).Select(match => match.Value).ToArray();
            var summaryWords = WordsPattern.Matches(summary).Select(match => match.Value).ToArray();
            var longest = 0;
            for (var outputIndex = 0; outputIndex < outputWords.Length; outputIndex++)
            {
                for (var summaryIndex = 0; summaryIndex < summaryWords.Length; summaryIndex++)
                {
                    var length = 0;
                    while (outputIndex + length < outputWords.Length
                        && summaryIndex + length < summaryWords.Length
                        && outputWords[outputIndex + length].Equals(
                            summaryWords[summaryIndex + length],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        length++;
                    }

                    longest = Math.Max(longest, length);
                    if (longest >= 5) return true;
                }
            }

            return false;
        }

        private static string AddLegalForm(string name) =>
            Regex.IsMatch(name, @"\b(LLC|Ltd|JSC|Inc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                ? name
                : $"{name} LLC";

        private static string IndustryCodeFor(string activity)
        {
            foreach (var industry in IndustryCodes)
            {
                if (industry.Pattern.IsMatch(activity)) return industry.Code;
            }

            return "46900";
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

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private static TenderSource Pick(IReadOnlyList<TenderSource> tenders) => tenders[Random.Shared.Next(tenders.Count)];

        private static CustomerProfileGenerationException InvalidProfile(string reason) =>
            new($"Amazon Bedrock returned an invalid customer profile: {reason} No customers were saved.");

        private sealed record CustomerIdentity(string CustomerId, string TaxCode);
        private sealed record GenerationPlan(string LanguageCode, bool MatchesTender, bool LoanEligible, TenderSource Tender)
        {
            public int MonthsWithBank => LoanEligible ? Random.Shared.Next(24, 61) : MatchesTender ? 18 : 6;
        }
        private sealed record TenderSource(string Summary, decimal StartPrice, string? RegionName, string? DeliveryRegion, string? DeliveryDistrict);
    }

    public sealed class NoAvailableTenderSummaryException()
        : Exception("Load at least one tender with an English summary before generating customers.");

    public sealed class CustomerProfileGenerationException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}
