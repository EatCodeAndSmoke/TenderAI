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
        private const int MaxNameAttempts = 3;
        private static readonly Regex CustomerIdPattern = new(@"\bC[\s_-]*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex CustomerReferencePattern = new(@"\b(customer|client|account)\s*(id|no\.?|number)?\s*[:#-]?\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        private static readonly string[] CompanyBrands = ["Navqiron", "Zarafshan", "Istiqlol", "Orzu", "Taraqqiyot", "Yangi Avlod", "Samarqand", "Baraka"];
        private static readonly string[] CompanyDescriptors = ["Meridian", "Alliance", "Pioneer", "Horizon", "Central", "Golden", "Crescent", "Evergreen"];
        private static readonly string[] CompanySectors = ["Trading", "Supplies", "Enterprise", "Industries", "Partners", "Commerce"];

        public async Task<IReadOnlyList<CustomerRecordDto>> GenerateAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow.AddHours(5);
            var storedTenders = await db.Tenders
                .AsNoTracking()
                .Include(tender => tender.Products)
                .Where(tender => tender.EndDate >= now)
                .Where(t => t.Summary != null && t.Summary != "")
                .ToListAsync(ct);
            var tenders = storedTenders
                .Where(tender => !string.IsNullOrWhiteSpace(tender.Summary))
                .Select(ToTenderSource)
                .Where(tender => tender.Products.Count > 0 && FindIndustry(tender.MatchingEvidence) is not null)
                .ToList();
            if (tenders.Count == 0)
            {
                throw new NoAvailableTenderSummaryException();
            }

            var existing = await db.Customers
                .AsNoTracking()
                .Select(c => new { c.CustomerId, c.TaxCode, c.Name })
                .ToListAsync(ct);
            var usedNames = existing
                .Select(c => NormalizeCompanyName(c.Name))
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usedPayments = (await db.CustomerPayments
                .AsNoTracking()
                .Select(payment => payment.PurposeDescription)
                .ToListAsync(ct))
                .Select(NormalizePaymentDescription)
                .Where(description => description.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var firstMatchingTender = Pick(tenders);
            var firstExpectedIndustry = FindIndustry(firstMatchingTender.MatchingEvidence);
            var secondMatchingTender = PickDistinctMatchingTender(tenders, firstMatchingTender, firstExpectedIndustry);
            var secondExpectedIndustry = FindIndustry(secondMatchingTender.MatchingEvidence);
            var canUseDistinctActivities = firstExpectedIndustry is not null
                && secondExpectedIndustry is not null
                && !StringComparer.Ordinal.Equals(firstExpectedIndustry.Code, secondExpectedIndustry.Code);
            var plans = new[]
            {
                new GenerationPlan("en", true, true, firstMatchingTender, firstExpectedIndustry, Random.Shared.Next(24, 61), false),
                new GenerationPlan("ru", true, false, secondMatchingTender, secondExpectedIndustry, 18, canUseDistinctActivities),
                new GenerationPlan("uz", false, false, Pick(tenders), null, 6, false)
            };

            // The first AI requests run concurrently; collisions are resolved in order below.
            var drafts = await Task.WhenAll(plans.Select(plan => RequestDraftAsync(plan, usedNames, ct)));
            var accepted = new List<ValidatedProfile>(plans.Length);
            foreach (var (plan, initialDraft) in plans.Zip(drafts))
            {
                var draft = initialDraft;
                string? companyName = null;
                for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
                {
                    companyName = NormalizeCompanyNameForDisplay(draft.CompanyName, plan.Tender.Summary);
                    var nameKey = companyName is null ? "" : NormalizeCompanyName(companyName);
                    if (companyName is not null && !usedNames.Contains(nameKey))
                    {
                        break;
                    }

                    companyName = null;
                    if (attempt < MaxNameAttempts)
                    {
                        var exclusions = usedNames.ToArray();
                        draft = await RequestDraftAsync(plan, exclusions, ct);
                    }
                }

                // Bad model text or repeated names are repaired locally; Bedrock request failures still propagate.
                companyName ??= CreateUniqueCompanyName(usedNames);
                var industry = ResolveIndustry(draft.IndustryName, plan, accepted);
                var paymentDescriptions = plan.MatchesTender
                    ? NormalizePaymentDescriptions(
                        draft.RecentPaymentDescriptions,
                        industry,
                        plan.Tender,
                        usedPayments)
                    : [];
                var profile = new ValidatedProfile(plan, companyName, industry, paymentDescriptions);
                accepted.Add(profile);
                usedNames.Add(NormalizeCompanyName(companyName));
                foreach (var description in paymentDescriptions)
                {
                    usedPayments.Add(NormalizePaymentDescription(description));
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
            profileGenerator.GenerateAsync(
                plan.Tender.MatchingEvidence,
                plan.LanguageCode,
                plan.MatchesTender,
                excludedNames,
                [],
                [],
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

        private static IndustryDefinition ResolveIndustry(
            string? modelActivity,
            GenerationPlan plan,
            IReadOnlyCollection<ValidatedProfile> accepted)
        {
            if (plan.MatchesTender && plan.ExpectedIndustry is not null)
            {
                return plan.ExpectedIndustry;
            }

            var proposed = FindIndustry(modelActivity ?? "") ?? GenericIndustry;
            if (!plan.MatchesTender)
            {
                var matchingCodes = accepted
                    .Where(profile => profile.Plan.MatchesTender)
                    .Select(profile => profile.Industry.Code)
                    .ToHashSet(StringComparer.Ordinal);
                if (matchingCodes.Contains(proposed.Code))
                {
                    proposed = AllIndustries().First(industry => !matchingCodes.Contains(industry.Code));
                }
            }

            return proposed;
        }

        private static IReadOnlyList<string> NormalizePaymentDescriptions(
            IReadOnlyList<string>? modelDescriptions,
            IndustryDefinition industry,
            TenderSource tender,
            IReadOnlySet<string> alreadyUsed)
        {
            if (modelDescriptions is { Count: 3 })
            {
                var cleaned = modelDescriptions
                    .Select(description => CleanPaymentDescription(description, tender.MatchingEvidence))
                    .ToArray();
                if (cleaned.All(description => description is not null)
                    && cleaned.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3
                    && ArePaymentDescriptionsVaried(cleaned!)
                    && cleaned.Take(2).All(description => ReferencesTenderProduct(description!, tender))
                    && cleaned.All(description => !alreadyUsed.Contains(NormalizePaymentDescription(description!))))
                {
                    return cleaned.Select(description => description!).ToArray();
                }
            }

            return CreateUniquePaymentDescriptions(tender, industry, alreadyUsed);
        }

        private static string? CleanPaymentDescription(string? value, string tenderSummary)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var cleaned = CustomerIdPattern.Replace(value, " ");
            cleaned = CustomerReferencePattern.Replace(cleaned, " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ',', '.', ';', ':', '-');
            if (cleaned.Length is < 8 or > 120
                || WordsPattern.Matches(cleaned).Count > 14
                || ContainsLongTenderPhrase(cleaned, tenderSummary))
            {
                return null;
            }

            return cleaned;
        }

        private static IReadOnlyList<string> CreateUniquePaymentDescriptions(
            TenderSource tender,
            IndustryDefinition industry,
            IReadOnlySet<string> alreadyUsed)
        {
            var productAnchor = tender.Products
                .Select(GetPaymentAnchor)
                .FirstOrDefault(anchor => !string.IsNullOrWhiteSpace(anchor))
                ?? industry.Description.ToLowerInvariant();
            var templates = new[]
            {
                $"Payment for {productAnchor} supply, contract No",
                $"Delivery of {productAnchor}, invoice No",
                $"Purchase order for {productAnchor}, contract No"
            };

            var result = new List<string>(3);
            var used = alreadyUsed.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var template in templates)
            {
                string description;
                do
                {
                    description = $"{template} {Random.Shared.Next(10_000, 1_000_000)}";
                } while (!used.Add(NormalizePaymentDescription(description)));

                result.Add(description);
            }

            return result;
        }

        private static bool ReferencesTenderProduct(string description, TenderSource tender)
        {
            var paymentWords = WordsPattern.Matches(description)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return tender.Products
                .SelectMany(ProductWords)
                .Any(paymentWords.Contains);
        }

        private static IEnumerable<string> ProductWords(TenderProductSource product)
        {
            foreach (var value in new[] { product.ProductName, product.CategoryName, product.Description })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (Match match in WordsPattern.Matches(value))
                {
                    if (match.Value.Length > 2 && !PaymentStopWords.Contains(match.Value))
                    {
                        yield return match.Value;
                    }
                }
            }
        }

        private static string? GetPaymentAnchor(TenderProductSource product)
        {
            var words = ProductWords(product)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();
            return words.Length == 0 ? null : string.Join(' ', words);
        }

        private static string? NormalizeCompanyNameForDisplay(string? value, string tenderSummary)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var name = CustomerIdPattern.Replace(value, " ");
            name = CustomerReferencePattern.Replace(name, " ");
            name = Regex.Replace(name, @"\s+", " ").Trim(' ', ',', '.', ';', ':', '-');
            if (name.Length is < 2 or > 160
                || WordsPattern.Matches(name).Count > 10
                || ContainsLongTenderPhrase(name, tenderSummary))
            {
                return null;
            }

            return AddLegalForm(name);
        }

        private static string CreateUniqueCompanyName(IReadOnlySet<string> usedNames)
        {
            while (true)
            {
                var candidate = $"{Pick(CompanyBrands)} {Pick(CompanyDescriptors)} {Pick(CompanySectors)} LLC";
                var key = NormalizeCompanyName(candidate);
                if (!usedNames.Contains(key)) return candidate;
            }
        }

        private static bool ArePaymentDescriptionsVaried(IReadOnlyList<string?> descriptions)
        {
            var wordSets = descriptions
                .Select(description => WordsPattern.Matches(description ?? "")
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
            withoutId = CustomerReferencePattern.Replace(withoutId, " ");
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

        private static IEnumerable<IndustryDefinition> AllIndustries() =>
            Industries.Select(industry => new IndustryDefinition(industry.Code, industry.Description))
                .Append(GenericIndustry);

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

        private static TenderSource ToTenderSource(TenderEntity tender)
        {
            var products = tender.Products
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductName))
                .OrderBy(product => product.OrderNum)
                .Select(product => new TenderProductSource(
                    product.ProductName,
                    product.CategoryName,
                    product.Description,
                    product.TechnicalSpecifications))
                .ToList();
            var matchingEvidence = string.Join(
                "\n",
                new[] { tender.Summary, tender.Name, tender.DetailedName }
                    .Concat(products.SelectMany(product => new[]
                    {
                        product.ProductName,
                        product.CategoryName,
                        product.Description,
                        product.TechnicalSpecifications
                    }))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            return new TenderSource(
                tender.Summary!,
                matchingEvidence,
                tender.StartPrice,
                tender.RegionName,
                tender.DeliveryRegion,
                tender.DeliveryDistrict,
                products);
        }

        private static TenderSource PickDistinctMatchingTender(
            IReadOnlyList<TenderSource> tenders,
            TenderSource first,
            IndustryDefinition? firstIndustry)
        {
            var distinctActivityTenders = tenders
                .Where(tender => !StringComparer.Ordinal.Equals(tender.MatchingEvidence, first.MatchingEvidence))
                .Where(tender =>
                {
                    var industry = FindIndustry(tender.MatchingEvidence);
                    return firstIndustry is not null
                        && industry is not null
                        && !StringComparer.Ordinal.Equals(firstIndustry.Code, industry.Code);
                })
                .ToList();
            if (distinctActivityTenders.Count > 0) return Pick(distinctActivityTenders);

            var differentSummaries = tenders
                .Where(tender => !StringComparer.Ordinal.Equals(tender.MatchingEvidence, first.MatchingEvidence))
                .ToList();
            return differentSummaries.Count > 0 ? Pick(differentSummaries) : first;
        }

        private static string Pick(string[] values) => values[Random.Shared.Next(values.Length)];

        private static readonly HashSet<string> PaymentStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "for", "the", "with", "from", "new", "supply", "purchase", "product", "products", "goods"
        };

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
            IReadOnlyList<string> PaymentDescriptions);
        private sealed record TenderProductSource(
            string ProductName,
            string? CategoryName,
            string? Description,
            string? TechnicalSpecifications);
        private sealed record TenderSource(
            string Summary,
            string MatchingEvidence,
            decimal StartPrice,
            string? RegionName,
            string? DeliveryRegion,
            string? DeliveryDistrict,
            IReadOnlyList<TenderProductSource> Products);
    }

    public sealed class NoAvailableTenderSummaryException()
        : Exception("Load at least one open tender with products and an activity that can be matched before generating customers.");

    public sealed class CustomerProfileGenerationException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}
