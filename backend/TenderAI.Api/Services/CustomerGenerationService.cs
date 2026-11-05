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
        private static readonly BusinessProfile[] Profiles =
        [
            new("10710", "Manufacture of bread", "fresh bread delivery", "bakery products", "flour and baking supplies"),
            new("46390", "Wholesale of food products", "food products delivery", "rice and groceries", "food stock"),
            new("46499", "Wholesale of paper and stationery", "office stationery supply", "paper products", "stationery stock"),
            new("81210", "General cleaning of buildings", "office cleaning services", "building cleaning services", "cleaning materials"),
            new("41000", "Building construction and repair", "building repair work", "construction services", "construction materials"),
            new("46460", "Wholesale of pharmaceutical goods", "pharmaceutical supplies", "medical products", "medical stock"),
            new("46510", "Wholesale of computers and peripheral equipment", "computer equipment supply", "laptops and accessories", "computer stock"),
            new("62010", "Computer programming activities", "software development services", "software maintenance", "software licenses"),
            new("46690", "Wholesale of machinery and equipment", "laboratory equipment supply", "machinery and equipment", "equipment stock"),
            new("46470", "Wholesale of furniture and lighting equipment", "office furniture supply", "lighting equipment", "furniture stock"),
            new("01110", "Growing of cereals and legumes", "wheat and grain supply", "dried legumes", "seeds and fertilizer"),
            new("01210", "Growing of grapes", "fresh grape supply", "table grapes", "vineyard supplies"),
            new("01410", "Raising of dairy cattle", "fresh milk supply", "dairy cattle products", "animal feed"),
            new("01610", "Support activities for crop production", "crop harvesting services", "field preparation services", "agricultural supplies"),
            new("14130", "Manufacture of outerwear", "workwear production", "uniform supply", "fabric and thread"),
            new("18120", "Printing services", "brochure printing", "printed forms and labels", "paper and ink"),
            new("22220", "Manufacture of plastic packing goods", "plastic packaging supply", "packaging containers", "plastic materials"),
            new("25110", "Manufacture of metal structures", "metal structure fabrication", "steel frames and components", "steel and fasteners"),
            new("31010", "Manufacture of office furniture", "office desk production", "office cabinets", "timber and fittings"),
            new("43210", "Electrical installation", "electrical installation work", "building wiring services", "electrical cables"),
            new("43220", "Plumbing and heating installation", "plumbing installation work", "heating system repairs", "pipes and fittings"),
            new("43320", "Joinery installation", "door and window installation", "interior joinery work", "doors and hardware"),
            new("49390", "Other passenger land transport", "staff transportation services", "passenger bus hire", "vehicle fuel"),
            new("49410", "Freight transport by road", "road freight delivery", "cargo transportation", "vehicle fuel"),
            new("52100", "Warehousing and storage", "warehouse storage services", "inventory handling services", "warehouse supplies"),
            new("56100", "Restaurants and mobile food service", "meal catering services", "prepared meals", "food ingredients"),
            new("71120", "Engineering activities and technical consultancy", "engineering design services", "technical consultancy", "engineering software"),
            new("73110", "Advertising agencies", "advertising campaign services", "marketing materials", "advertising supplies"),
            new("77320", "Renting of construction machinery", "construction equipment rental", "excavator rental", "machinery maintenance"),
            new("80100", "Private security activities", "building security services", "security patrol services", "security equipment"),
            new("81300", "Landscape service activities", "landscaping services", "garden maintenance", "plants and gardening supplies"),
            new("86210", "General medical practice", "medical consultation services", "health examination services", "medical supplies")
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
                var profile = Pick(Profiles.Where(candidate => profileCounts.GetValueOrDefault(candidate.Code) == minimumCount).ToArray());
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
                        PreApprovedLimit = Random.Shared.Next(100, 301) * 1_000_000L,
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

        private sealed record BusinessProfile(string Code, string Name, string FirstSale, string SecondSale, string Purchase);
    }
}
