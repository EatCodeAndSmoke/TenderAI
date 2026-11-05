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
            new("46470", "Wholesale of furniture and lighting equipment", "office furniture supply", "lighting equipment", "furniture stock")
        ];

        public async Task<IReadOnlyList<CustomerRecordDto>> GenerateAsync(CancellationToken ct)
        {
            await GenerationLock.WaitAsync(ct);
            try
            {
                var existing = await db.Customers.AsNoTracking()
                    .Select(customer => new { customer.CustomerId, customer.TaxCode, customer.Name })
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
                var profile = Pick(Profiles);
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
