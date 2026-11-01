using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Data
{
    // Demo data, taken from the frontends' former mock data. Added once, when the database is created.
    public static class SeedData
    {
        public static void Seed(TenderAiDbContext db)
        {
            var now = DateTime.UtcNow;

            db.Customers.AddRange(
                new CustomerEntity
                {
                    CustomerId = "C-01",
                    Name = "Non Baraka LLC",
                    TaxCode = "301234567",
                    Type = "LLC",
                    IndustryCode = "10710",
                    IndustryName = "Manufacture of bread",
                    RegionName = "Tashkent",
                    DistrictName = "Yunusobod",
                    LanguageCode = "en",
                    RegistrationDate = new DateOnly(2019, 3, 4),
                    CustomerSince = new DateOnly(2024, 8, 12),
                    RiskFeatures = new CustomerRiskFeaturesEntity
                    {
                        CustomerId = "C-01",
                        AvgMonthlyInflow6m = 120_000_000,
                        OutstandingAmount = 85_000_000,
                        PreApprovedLimit = 150_000_000,
                        KycPassed = true
                    },
                    Payments =
                    [
                        Payment("C-01", 2026, 10, 8, true, "Payment for bread delivery, contract No 14", 18_500_000),
                        Payment("C-01", 2026, 9, 14, true, "Kindergarten No 7: payment for bread, September", 6_200_000),
                        Payment("C-01", 2026, 9, 3, false, "Purchase of flour, invoice 2231", 22_000_000),
                        // Older than 12 months, so the matcher ignores it
                        Payment("C-01", 2025, 6, 20, true, "Payment for wedding catering", 4_500_000)
                    ],
                    Notifications =
                    [
                        new NotificationEntity
                        {
                            Id = "N-101",
                            CustomerId = "C-01",
                            LotId = "T-1001",
                            CreatedAt = now.AddMinutes(-25),
                            Title = "Bread supply for schools of Yunusobod district",
                            Body = "Yunusobod district's school education department is buying fresh bread for 12 schools for the school year. "
                                + "Bread must arrive every day before 8:00, and the bakery needs a sanitary certificate. Bids close on 30 November 2026.",
                            Buyer = "Yunusobod district school education department",
                            Region = "Tashkent",
                            StartPrice = 240_000_000,
                            Deadline = new DateOnly(2026, 11, 30),
                            PaymentTermsDays = 30,
                            Requirements =
                            [
                                "Daily delivery to 12 schools before 8:00",
                                "Sanitary certificate for the bakery",
                                "At least 1 year in business"
                            ],
                            WhyText = "You already deliver bread to Kindergarten No 7, you're based in Yunusobod, and this contract fits the size of your business.",
                            Offer = new LoanOfferEntity { Financing = 120_000_000, Guarantee = 7_200_000, PreApprovedLimit = 150_000_000 }
                        },
                        new NotificationEntity
                        {
                            Id = "N-102",
                            CustomerId = "C-01",
                            LotId = "T-1002",
                            CreatedAt = now.AddMinutes(-140),
                            Title = "Bread for kindergarten No. 12",
                            Body = "Kindergarten No. 12 is buying fresh bread for its children. Deliveries are daily, "
                                + "and the bakery needs a sanitary certificate. Bids close on 20 November 2026.",
                            Buyer = "Kindergarten No. 12",
                            Region = "Tashkent",
                            StartPrice = 60_000_000,
                            Deadline = new DateOnly(2026, 11, 20),
                            PaymentTermsDays = 30,
                            Requirements =
                            [
                                "Daily delivery before 8:00",
                                "Sanitary certificate for the bakery"
                            ],
                            WhyText = "You already deliver bread to a kindergarten, and this small contract is easy for a bakery of your size.",
                            Offer = new LoanOfferEntity { Financing = 30_000_000, Guarantee = 1_800_000, PreApprovedLimit = 150_000_000 }
                        }
                    ]
                },
                new CustomerEntity
                {
                    CustomerId = "C-02",
                    Name = "Sariq Non LLC",
                    TaxCode = "302345678",
                    Type = "LLC",
                    IndustryCode = "10710",
                    IndustryName = "Manufacture of bread",
                    RegionName = "Tashkent",
                    DistrictName = "Chilonzor",
                    LanguageCode = "en",
                    RegistrationDate = new DateOnly(2020, 6, 15),
                    CustomerSince = new DateOnly(2025, 4, 2),
                    RiskFeatures = new CustomerRiskFeaturesEntity
                    {
                        CustomerId = "C-02",
                        AvgMonthlyInflow6m = 90_000_000,
                        OutstandingAmount = 40_000_000,
                        MaxOverdueDays12m = 45,
                        PreApprovedLimit = 150_000_000,
                        KycPassed = true
                    },
                    Payments =
                    [
                        Payment("C-02", 2026, 10, 2, true, "Payment for bread and buns, cafe contract No 3", 9_800_000),
                        Payment("C-02", 2026, 8, 21, false, "Purchase of flour and yeast, invoice 884", 12_400_000)
                    ]
                });

            db.SaveChanges();
        }

        private static CustomerPaymentEntity Payment(string customerId, int year, int month, int day, bool isIncoming, string purpose, long amount) =>
            new()
            {
                CustomerId = customerId,
                Date = new DateOnly(year, month, day),
                IsIncoming = isIncoming,
                PurposeDescription = purpose,
                Amount = amount
            };
    }
}
