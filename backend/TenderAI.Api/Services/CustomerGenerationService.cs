using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class CustomerGenerationService(TenderAiDbContext db)
    {
        private static readonly SemaphoreSlim GenerationLock = new(1, 1);
        private static readonly string[] CompanyNames = ["Orzu", "Navqiron", "Zarafshan", "Istiqlol", "Yangi Avlod", "Taraqqiyot", "Baraka", "Samarqand"];
        private static readonly string[] CompanyTypes = ["Trade", "Supplies", "Enterprise", "Partners", "Industries", "Commerce"];
        private static readonly string[] Regions =
        [
            "Tashkent", "Tashkent region", "Andijan", "Bukhara", "Fergana", "Jizzakh", "Karakalpakstan",
            "Kashkadarya", "Khorezm", "Namangan", "Navoi", "Samarkand", "Surkhandarya", "Syrdarya"
        ];
        private static readonly string[] Districts = ["Yunusobod", "Chilonzor", "Mirzo Ulugbek", "Sergeli", "Yakkasaray"];
        private static readonly string[] Languages = ["en", "ru", "uz"];
        internal static IReadOnlyList<BusinessProfile> Profiles { get; } =
        [
            new("46710", "Wholesale of fuels and petroleum products for refineries", "straight-run gasoil supply for refinery processing", "crude oil and gas condensate supply", "petroleum stock"),
            new("46750", "Wholesale of chemical fuel additives", "methyl tert-butyl ether (MTBE) supply", "monomethylaniline (MMA) fuel additive supply", "industrial chemical stock"),
            new("41000", "Building construction and repair", "college building reconstruction work", "school facade and roof renovation", "construction materials"),
            new("41000", "Building construction and repair", "office building renovation work", "building facade repair and window replacement", "construction materials"),
            new("42990", "Construction of civil engineering projects", "hydroelectric power plant construction", "dam and spillway construction", "construction materials"),
            new("42220", "Construction of power and telecommunications lines", "high-voltage transmission line construction", "electric power line construction work", "power line cables and supports"),
            new("71120", "Engineering consultancy for road projects", "road reconstruction feasibility study", "highway technical-economic feasibility assessment", "engineering software"),
            new("71120", "Engineering consultancy and project management for hydropower", "hydroelectric power plant project management", "hydropower construction engineering consultancy", "engineering software"),
            new("70220", "Business and management consultancy", "housing project finance advisory services", "escrow account implementation consultancy", "business research"),
            new("46439", "Wholesale of household electrical goods", "air conditioners and refrigerator supply", "electric stoves and water cooler supply", "household appliance stock"),
            new("46460", "Wholesale of medicines including immunoglobulin and oncology drugs", "immunoglobulin medicine supply", "carboplatin infusion medicine supply", "pharmaceutical stock"),
            new("46460", "Wholesale of pharmaceutical creams and ointments", "dexpanthenol cream and ointment supply", "mupirocin ointment supply", "pharmaceutical stock"),
            new("46460", "Wholesale of diagnostic screening and PCR reagents", "neonatal screening reagent kits supply", "PCR diagnostic reagents and test kits supply", "diagnostic reagent stock"),
            new("46460", "Wholesale of diagnostic tests and allergens", "hepatitis, syphilis and HIV diagnostic test kits supply", "tuberculosis diagnostic allergen supply", "medical test stock"),
            new("46690", "Wholesale and installation of automatic weather stations and meteorological equipment", "automatic weather stations supply and installation", "weather monitoring stations and sensors supply and installation", "meteorological equipment stock"),
            new("46690", "Wholesale of radiation and electrical metrology equipment", "solar radiation measurement standard supply", "voltage measurement standard supply", "metrology instrument stock"),
            new("46690", "Wholesale of frequency and radiofrequency metrology equipment", "time and frequency standards supply", "radiofrequency sensor calibration system supply", "metrology instrument stock"),
            new("46690", "Wholesale of measurement and audiometry standards", "audiometry measurement standard supply", "connection duration measurement system supply", "metrology instrument stock"),
            new("46690", "Wholesale of medical endoscopy equipment", "gastroendoscope and videocolonoscope supply", "bronchoscope and medical videoendoscope supply", "medical equipment stock"),
            new("46690", "Wholesale of medical endoscopy equipment", "hysteroscope and arthroscope supply", "laparoscope and videoendoscopy equipment supply", "medical equipment stock"),
            new("46690", "Wholesale of cardiac monitoring and ECG equipment", "electrocardiogram (ECG) hospital equipment supply", "cardiac monitoring equipment supply", "medical equipment stock"),
            new("46690", "Wholesale of blood collection and donor processing equipment", "blood collection and storage containers supply", "donor blood processing kits supply", "medical consumables stock"),
            new("46690", "Wholesale of industrial waste processing machinery", "industrial metal waste shredder line supply", "industrial waste press-compactor equipment supply", "industrial machinery stock"),
            new("46690", "Wholesale of industrial gas turbine engines", "gas turbine engine supply", "industrial gas turbine equipment supply", "turbine equipment stock"),
            new("46690", "Wholesale of automated bank deposit machines", "automated bank deposit machines supply", "cash deposit machine supply", "banking equipment stock"),
            new("46690", "Wholesale of commercial kitchen equipment", "commercial kitchen equipment supply", "school kitchen food distribution equipment supply", "commercial kitchen equipment stock"),
            new("46510", "Wholesale of computers and peripheral equipment", "monoblock computer supply", "laptop computer supply", "computer stock"),
            new("46520", "Wholesale of electronic and telecommunications equipment", "video conference equipment and webcam supply", "video conferencing cameras supply", "telecommunications equipment stock"),
            new("45190", "Sale of other motor vehicles", "25-ton mobile crane vehicle supply", "airport aircraft de-icing multilift vehicle supply", "commercial vehicle stock"),
            new("31010", "Manufacture of office and shop furniture", "school student desks and chairs production", "school bookcases and office desks production", "timber and furniture fittings"),
            new("62010", "Computer programming activities", "financial management information system development", "customs finance management software development", "software licenses"),
            new("80200", "Security systems service activities", "AI video surveillance system installation", "video surveillance monitoring system installation", "security cameras"),
            new("81300", "Landscape service activities", "drought-resistant garden landscaping", "dryland garden establishment services", "plants and gardening supplies"),
            new("33120", "Repair of machinery and equipment", "industrial gas generator maintenance services", "gas turbine generator repair and maintenance", "generator spare parts")
        ];

        public async Task<IReadOnlyList<CustomerRecordDto>> GenerateAsync(CancellationToken ct)
        {
            await GenerationLock.WaitAsync(ct);
            try
            {
                var existing = await db.Customers.AsNoTracking()
                    .Select(customer => new { customer.CustomerId, customer.TaxCode, customer.Name, customer.IndustryCode })
                    .ToListAsync(ct);
                var ids = existing.Select(customer => customer.CustomerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var taxCodes = existing.Select(customer => customer.TaxCode).ToHashSet(StringComparer.Ordinal);
                var names = existing.Select(customer => customer.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var number = 1;
                while (ids.Contains($"C-{number:D2}")) number++;
                var customerId = $"C-{number:D2}";
                string taxCode;
                do
                {
                    taxCode = Random.Shared.Next(100_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture);
                } while (!taxCodes.Add(taxCode));

                var name = "";
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    name = $"{Pick(CompanyNames)} {Pick(CompanyTypes)} LLC";
                    if (!names.Contains(name)) break;
                }
                if (names.Contains(name))
                {
                    var suffix = number;
                    do { name = $"Navqiron Trade {suffix++} LLC"; } while (names.Contains(name));
                }

                var today = DateOnly.FromDateTime(DateTime.Today);
                var monthsWithBank = Random.Shared.Next(6, 61);
                var profileCounts = existing.GroupBy(customer => customer.IndustryCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                var minimumCount = Profiles.Min(candidate => profileCounts.GetValueOrDefault(candidate.Code));
                var eligibleCodes = Profiles.Where(candidate => profileCounts.GetValueOrDefault(candidate.Code) == minimumCount)
                    .Select(candidate => candidate.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var code = Pick(eligibleCodes);
                var profile = Pick(Profiles.Where(candidate => candidate.Code == code).ToArray());
                var overdue = Random.Shared.Next(3) == 0;
                var customer = new CustomerEntity
                {
                    CustomerId = customerId,
                    Name = name,
                    TaxCode = taxCode,
                    Type = "LLC",
                    IndustryCode = profile.Code,
                    IndustryName = profile.Name,
                    RegionName = Pick(Regions),
                    DistrictName = Pick(Districts),
                    LanguageCode = Pick(Languages),
                    RegistrationDate = today.AddMonths(-(monthsWithBank + Random.Shared.Next(12, 49))),
                    CustomerSince = today.AddMonths(-monthsWithBank),
                    RiskFeatures = new CustomerRiskFeaturesEntity
                    {
                        CustomerId = customerId,
                        AvgMonthlyInflow6m = Random.Shared.Next(80, 201) * 1_000_000L,
                        OutstandingAmount = 0,
                        OverdueAmount = overdue ? 1_000_000 : 0,
                        MaxOverdueDays12m = overdue ? 45 : 0,
                        PreApprovedLimit = Random.Shared.Next(2, 61) * 1_000_000_000L,
                        KycPassed = true
                    },
                    Payments =
                    [
                        Payment(customerId, today.AddDays(-7), true, $"Payment for {profile.FirstSale}, contract No 14"),
                        Payment(customerId, today.AddDays(-37), true, $"Payment for {profile.SecondSale}, invoice No 25"),
                        Payment(customerId, today.AddDays(-68), false, $"Payment for {profile.Purchase}")
                    ],
                    Accounts = DemoAccounts.Create(customerId)
                };
                db.Customers.Add(customer);
                await db.SaveChangesAsync(ct);
                return [customer.ToDto(today)];
            }
            finally
            {
                GenerationLock.Release();
            }
        }

        private static CustomerPaymentEntity Payment(string customerId, DateOnly date, bool incoming, string purpose) => new()
        {
            CustomerId = customerId,
            Date = date,
            IsIncoming = incoming,
            PurposeDescription = purpose,
            Amount = Random.Shared.Next(2, 31) * 1_000_000L
        };

        private static T Pick<T>(IReadOnlyList<T> values) => values[Random.Shared.Next(values.Count)];

        internal sealed record BusinessProfile(string Code, string Name, string FirstSale, string SecondSale, string Purchase);
    }
}
