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
        private const int MaxProfileAttempts = 3;
        private static readonly Regex CustomerIdPattern = new(@"\bC[\s_-]*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WordsPattern = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);
        private static readonly Regex LegalFormPattern = new(@"\b(LLC|Ltd|JSC|Inc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly (Regex Pattern, string Code, string Description)[] Industries =
        [
            (new(@"\b(bread|bakery|bakeries|flour|buns?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "10710", "Manufacture of bread"),
            (new(@"\b(food|foods|grocery|groceries|rice|meat|vegetable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46390", "Wholesale of food products"),
            (new(@"\b(paper|stationery|office supplies)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46499", "Wholesale of paper and stationery"),
            (new(@"\b(cleaning|janitorial)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "81210", "General cleaning of buildings"),
            (new(@"\b(roof|repair|construction|renovation)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "41000", "Building construction and repair"),
            (new(@"\b(medicine|medical|pharmaceutical)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46460", "Wholesale of pharmaceutical goods"),
            (new(@"\b(computer|laptop|hardware)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46510", "Wholesale of computers and peripheral equipment"),
            (new(@"\b(software|information technology)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "62010", "Computer programming activities"),
            (new(@"\b(laboratory|in-vitro|diagnostic|microscope|technical unit|equipment|machinery|tools)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46690", "Wholesale of other machinery and equipment"),
            (new(@"\b(agricultur|greenhouse|farming|irrigation)\w*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46610", "Wholesale of agricultural machinery"),
            (new(@"\b(furniture|carpet|lighting)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46470", "Wholesale of furniture and lighting equipment"),
            (new(@"\b(clothing|garment|footwear|uniform|textile)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "46420", "Wholesale of clothing and footwear"),
            (new(@"\b(fuel|petrol|gasoline|diesel)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "47300", "Retail sale of automotive fuel")
        ];
        private static readonly IndustryDefinition GenericIndustry = new("46900", "Non-specialized wholesale trade");

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

            var existing = await db.Customers
                .AsNoTracking()
                .Select(c => new { c.CustomerId, c.TaxCode, c.Name })
                .ToListAsync(ct);
            var existingNames = existing.Select(c => NormalizeCompanyName(c.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usedPaymentDescriptions = (await db.CustomerPayments
                .AsNoTracking()
                .Select(payment => payment.PurposeDescription)
                .ToListAsync(ct))
                .Select(NormalizePaymentDescription)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var firstMatchingTender = Pick(tenders);
            var firstExpectedIndustry = FindIndustry(firstMatchingTender.Summary);
            var secondMatchingTender = PickDistinctMatchingTender(tenders, firstMatchingTender, firstExpectedIndustry);
            var secondExpectedIndustry = FindIndustry(secondMatchingTender.Summary);
            var canUseDistinctActivities = firstExpectedIndustry is not null
                && secondExpectedIndustry is not null
                && !StringComparer.Ordinal.Equals(firstExpectedIndustry.Code, secondExpectedIndustry.Code);
            var unmatchedTender = Pick(tenders);
            var plans = new[]
            {
                new GenerationPlan("en", true, true, firstMatchingTender, firstExpectedIndustry, Random.Shared.Next(24, 61), false),
                new GenerationPlan("ru", true, false, secondMatchingTender, secondExpectedIndustry, 18, canUseDistinctActivities),
                new GenerationPlan("uz", false, false, unmatchedTender, null, 6, false)
            };

            var initialDrafts = await Task.WhenAll(plans.Select(plan => RequestDraftAsync(
                plan,
                existingNames,
                ct)));
            var accepted = new List<ValidatedProfile>(plans.Length);
            foreach (var (plan, initialDraft) in plans.Zip(initialDrafts))
            {
                var draft = initialDraft;
                for (var attempt = 1; attempt <= MaxProfileAttempts; attempt++)
                {
                    var validated = ValidateDraft(draft, plan);
                    var normalizedName = NormalizeCompanyName(validated.CompanyName);
                    var activityMismatch = plan.ExpectedIndustry is not null
                        && !StringComparer.Ordinal.Equals(plan.ExpectedIndustry.Code, validated.Industry.Code);
                    var duplicateName = existingNames.Contains(normalizedName)
                        || accepted.Any(profile => StringComparer.OrdinalIgnoreCase.Equals(profile.NormalizedName, normalizedName));
                    var duplicateActivity = plan.RequireDistinctActivity
                        && accepted.Any(profile => StringComparer.Ordinal.Equals(profile.Industry.Code, validated.Industry.Code));
                    var nonMatchingActivityOverlap = !plan.MatchesTender
                        && accepted.Any(profile => profile.Plan.MatchesTender
                            && StringComparer.Ordinal.Equals(profile.Industry.Code, validated.Industry.Code));
                    var duplicatePayment = validated.PaymentDescriptions.Any(payment =>
                        usedPaymentDescriptions.Contains(NormalizePaymentDescription(payment)));
                    if (!activityMismatch
                        && !duplicateName
                        && !duplicateActivity
                        && !nonMatchingActivityOverlap
                        && !duplicatePayment)
                    {
                        accepted.Add(validated with { NormalizedName = normalizedName });
                        foreach (var payment in validated.PaymentDescriptions)
                        {
                            usedPaymentDescriptions.Add(NormalizePaymentDescription(payment));
                        }
                        break;
                    }

                    if (attempt >= MaxProfileAttempts)
                    {
                        var issue = duplicateName
                            ? "a duplicate company name"
                            : activityMismatch ? "an activity that does not match its tender"
                            : duplicateActivity || nonMatchingActivityOverlap ? "a duplicate OKED activity"
                            : "a duplicate payment description";
                        throw new CustomerProfileGenerationException(
                            $"Amazon Bedrock couldn't generate a unique profile after {MaxProfileAttempts} attempts ({issue}). No customers were saved.");
                    }

                    var excludedNames = existingNames
                        .Concat(accepted.Select(profile => profile.NormalizedName))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var excludedActivities = accepted
                        .Select(profile => profile.Industry.Description)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var retryPayments = usedPaymentDescriptions.ToArray();
                    draft = await RequestDraftAsync(plan, excludedNames, excludedActivities, retryPayments, ct);
                }
            }

            var taxCodes = new HashSet<string>(existing.Select(c => c.TaxCode), StringComparer.Ordinal);
            var nextNumber = existing.Select(c => CustomerNumber(c.CustomerId)).DefaultIfEmpty(0).Max() + 1;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var customers = accepted.Select((profile, index) => CreateCustomer(
                new CustomerIdentity($"C-{nextNumber + index:D2}", NextTaxCode(taxCodes)),
                profile,
                today)).ToList();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Customers.AddRange(customers);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return customers.Select(customer => customer.ToDto(today)).ToList();
        }

        private Task<CustomerProfileDraft> RequestDraftAsync(
            GenerationPlan plan,
            IReadOnlyCollection<string> excludedNames,
            CancellationToken ct) =>
            RequestDraftAsync(plan, excludedNames, [], [], ct);

        private Task<CustomerProfileDraft> RequestDraftAsync(
            GenerationPlan plan,
            IReadOnlyCollection<string> excludedNames,
            IReadOnlyCollection<string> excludedActivities,
            IReadOnlyCollection<string> excludedPaymentDescriptions,
            CancellationToken ct) =>
            profileGenerator.GenerateAsync(
                plan.Tender.Summary,
                plan.LanguageCode,
                plan.MatchesTender,
                excludedNames,
                excludedActivities,
                excludedPaymentDescriptions,
                ct);

        private static CustomerEntity CreateCustomer(CustomerIdentity identity, ValidatedProfile profile, DateOnly today)
        {
            var plan = profile.Plan;
            var tender = plan.Tender;
            var monthlyInflow = plan.MatchesTender
                ? Math.Max(80_000_000L, CeilingToLong(tender.StartPrice / 3m))
                : Random.Shared.Next(20, 71) * 1_000_000L;
            var preApprovedLimit = plan.LoanEligible
                ? Math.Max(Random.Shared.Next(100, 301) * 1_000_000L, CeilingToLong(tender.StartPrice / 2m))
                : plan.MatchesTender ? 150_000_000L : (long?)null;

            return new CustomerEntity
            {
                CustomerId = identity.CustomerId,
                Name = profile.CompanyName,
                TaxCode = identity.TaxCode,
                Type = "LLC",
                IndustryCode = profile.Industry.Code,
                IndustryName = profile.Industry.Description,
                RegionName = FirstNonBlank(tender.DeliveryRegion, tender.RegionName) ?? "Tashkent",
                DistrictName = FirstNonBlank(tender.DeliveryDistrict) ?? "Yunusobod",
                LanguageCode = plan.LanguageCode,
                RegistrationDate = today.AddMonths(-(plan.MonthsWithBank + Random.Shared.Next(12, 49))),
                CustomerSince = today.AddMonths(-plan.MonthsWithBank),
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
                    ? profile.PaymentDescriptions.Select((description, index) => new CustomerPaymentEntity
                    {
                        CustomerId = identity.CustomerId,
                        Date = today.AddDays(new[] { -7, -37, -68 }[index]),
                        IsIncoming = index != 2,
                        PurposeDescription = description,
                        Amount = Random.Shared.Next(2, 31) * 1_000_000L
                    }).ToList()
                    : []
            };
        }

        private static ValidatedProfile ValidateDraft(CustomerProfileDraft draft, GenerationPlan plan)
        {
            var companyName = draft.CompanyName?.Trim();
            if (string.IsNullOrWhiteSpace(companyName)
                || companyName.Length > 160
                || WordsPattern.Matches(companyName).Count > 10)
            {
                throw InvalidProfile("The generated company name is missing or too long.");
            }

            var industryName = draft.IndustryName?.Trim();
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

            var paymentDescriptions = draft.RecentPaymentDescriptions;
            if (plan.MatchesTender)
            {
                if (paymentDescriptions is null || paymentDescriptions.Count != 3)
                {
                    throw InvalidProfile("A matching profile must contain exactly three recent payment descriptions.");
                }

                if (paymentDescriptions.Any(payment => string.IsNullOrWhiteSpace(payment)
                    || payment.Trim().Length > 120
                    || WordsPattern.Matches(payment).Count > 14))
                {
                    throw InvalidProfile("A generated payment description is missing or too long.");
                }

                var payments = paymentDescriptions.Select(payment => payment.Trim()).ToArray();
                if (payments.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3
                    || !ArePaymentDescriptionsVaried(payments))
                {
                    throw InvalidProfile("Recent payment descriptions must be distinct and meaningfully varied.");
                }

                if (payments.Any(payment => ContainsCustomerId(payment) || ContainsLongTenderPhrase(payment, plan.Tender.Summary)))
                {
                    throw InvalidProfile("A payment description contains an ID or copied tender wording.");
                }

                return new ValidatedProfile(
                    plan,
                    AddLegalForm(companyName),
                    ResolveIndustry(industryName),
                    payments);
            }

            if (paymentDescriptions is { Count: > 0 })
            {
                throw InvalidProfile("The non-matching profile must not contain tender-related payments.");
            }

            return new ValidatedProfile(plan, AddLegalForm(companyName), ResolveIndustry(industryName), []);
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

                    if (length >= 5) return true;
                }
            }

            return false;
        }

        private static string NormalizeCompanyName(string name)
        {
            var withoutId = CustomerIdPattern.Replace(name, " ");
            var withoutLegalForm = LegalFormPattern.Replace(withoutId, " ");
            return string.Join(' ', WordsPattern.Matches(withoutLegalForm)
                .Select(match => match.Value.ToLowerInvariant()));
        }

        private static string NormalizePaymentDescription(string description) =>
            string.Join(' ', WordsPattern.Matches(description)
                .Select(match => match.Value.ToLowerInvariant()));

        private static string AddLegalForm(string name) =>
            LegalFormPattern.IsMatch(name) ? name : $"{name} LLC";

        private static IndustryDefinition? FindIndustry(string text)
        {
            foreach (var industry in Industries)
            {
                if (industry.Pattern.IsMatch(text))
                {
                    return new IndustryDefinition(industry.Code, industry.Description);
                }
            }

            return null;
        }

        private static IndustryDefinition ResolveIndustry(string activity) =>
            FindIndustry(activity) ?? GenericIndustry;

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

        private static TenderSource PickDistinctMatchingTender(
            IReadOnlyList<TenderSource> tenders,
            TenderSource first,
            IndustryDefinition? firstIndustry)
        {
            var distinctActivityTenders = tenders
                .Where(tender => !StringComparer.Ordinal.Equals(tender.Summary, first.Summary))
                .Where(tender =>
                {
                    var industry = FindIndustry(tender.Summary);
                    return firstIndustry is not null
                        && industry is not null
                        && !StringComparer.Ordinal.Equals(firstIndustry.Code, industry.Code);
                })
                .ToList();
            if (distinctActivityTenders.Count > 0) return Pick(distinctActivityTenders);

            var differentSummaries = tenders
                .Where(tender => !StringComparer.Ordinal.Equals(tender.Summary, first.Summary))
                .ToList();
            return differentSummaries.Count > 0 ? Pick(differentSummaries) : first;
        }

        private static CustomerProfileGenerationException InvalidProfile(string reason) =>
            new($"Amazon Bedrock returned an invalid customer profile: {reason} No customers were saved.");

        private sealed record CustomerIdentity(string CustomerId, string TaxCode);
        private sealed record IndustryDefinition(string Code, string Description);
        private sealed record GenerationPlan(
            string LanguageCode,
            bool MatchesTender,
            bool LoanEligible,
            TenderSource Tender,
            IndustryDefinition? ExpectedIndustry,
            int MonthsWithBank,
            bool RequireDistinctActivity);
        private sealed record ValidatedProfile(
            GenerationPlan Plan,
            string CompanyName,
            IndustryDefinition Industry,
            IReadOnlyList<string> PaymentDescriptions,
            string NormalizedName = "");
        private sealed record TenderSource(string Summary, decimal StartPrice, string? RegionName, string? DeliveryRegion, string? DeliveryDistrict);
    }

    public sealed class NoAvailableTenderSummaryException()
        : Exception("Load at least one tender with an English summary before generating customers.");

    public sealed class CustomerProfileGenerationException(
        string message,
        Exception? innerException = null)
        : Exception(message, innerException);
}
