using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Controllers;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using Xunit;

namespace TenderAI.Api.Tests;

public class CustomerGenerationTests
{
    [Fact]
    public async Task GeneratePersistsThreeCustomersRiskFeaturesAndRecentPayments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new TenderAiDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Customers.Add(new CustomerEntity
            {
                CustomerId = "C-01",
                Name = "Existing customer",
                TaxCode = "300000001",
                Type = "LLC",
                IndustryCode = "10710",
                IndustryName = "Manufacture of bread",
                RegionName = "Tashkent",
                LanguageCode = "en",
                CustomerSince = new DateOnly(2020, 1, 1),
                RiskFeatures = new CustomerRiskFeaturesEntity { CustomerId = "C-01", KycPassed = true }
            });
            db.Tenders.AddRange(
                CreateTender(101, "The buyer is purchasing bread for public schools."),
                CreateTender(102, "Supply of food products to regional hospitals."));
            await db.SaveChangesAsync();

            var controller = new CustomersController(db);
            var response = await controller.Generate(
                new CustomerGenerationService(db, new FakeProfileGenerator()),
                CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(response.Result);
            var returned = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();

            Assert.Equal(3, returned.Count);
            Assert.All(returned, customer => Assert.NotNull(customer.Customer));
        }

        await using var verificationDb = new TenderAiDbContext(options);
        var persisted = await verificationDb.Customers
            .Include(customer => customer.RiskFeatures)
            .Include(customer => customer.Payments)
            .Where(customer => customer.CustomerId != "C-01")
            .OrderBy(customer => customer.CustomerId)
            .ToListAsync();

        Assert.Equal(4, await verificationDb.Customers.CountAsync());
        Assert.Equal("Existing customer", await verificationDb.Customers
            .Where(customer => customer.CustomerId == "C-01")
            .Select(customer => customer.Name)
            .SingleAsync());
        Assert.Equal(6, await verificationDb.CustomerPayments.CountAsync());

        var matched = persisted.Where(customer => !customer.OnRegister).ToList();
        Assert.Equal(2, matched.Count);
        Assert.Equal(new[] { "en", "ru" }, matched.Select(customer => customer.LanguageCode));
        Assert.All(persisted, customer =>
        {
            Assert.DoesNotContain("C-", customer.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C-", customer.IndustryName, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(matched, customer =>
        {
            Assert.Contains(customer.IndustryCode, new[] { "10710", "46390" });
            Assert.Equal(3, customer.Payments.Count);
            Assert.All(customer.Payments, payment =>
            {
                Assert.InRange(payment.PurposeDescription.Length, 8, 120);
                Assert.DoesNotContain("C-0", payment.PurposeDescription, StringComparison.OrdinalIgnoreCase);
            });
        });
        Assert.All(matched, customer => Assert.Contains(customer.Payments, payment =>
            payment.PurposeDescription.Contains("bread", StringComparison.OrdinalIgnoreCase)
            || payment.PurposeDescription.Contains("food", StringComparison.OrdinalIgnoreCase)));

        var unmatched = Assert.Single(persisted, customer => customer.OnRegister);
        Assert.Empty(unmatched.Payments);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var financingEligible = matched.Where(customer => MeetsFinancingTerms(customer, today)).ToList();
        var financingIneligible = matched.Where(customer => !MeetsFinancingTerms(customer, today)).ToList();
        Assert.Single(financingEligible);
        var overdueCustomer = Assert.Single(financingIneligible);
        Assert.Equal(45, overdueCustomer.RiskFeatures!.MaxOverdueDays12m);
        Assert.True(overdueCustomer.RiskFeatures.OverdueAmount > 0);
    }

    [Fact]
    public async Task GenerateWithoutTenderSummariesReturnsBadRequestWithoutSaving()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FakeProfileGenerator()),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task GenerateRejectsCustomerIdsInGeneratedNamesWithoutPersisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        const string summary = "The buyer wants to purchase special bread for the public schools this winter.";
        db.Tenders.Add(CreateTender(201, summary));
        await db.SaveChangesAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FakeProfileGenerator(invalidCompanyName: "Navqiron Supplies C 99")),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task GenerateRejectsIndustryCopiedFromTenderWithoutPersisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        const string summary = "The buyer wants to purchase special bread for the public schools this winter.";
        db.Tenders.Add(CreateTender(202, summary));
        await db.SaveChangesAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FakeProfileGenerator(invalidIndustryName: "Buyer wants to purchase special bread")),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task GenerateRejectsPaymentDescriptionCopiedFromTenderWithoutPersisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        const string summary = "The buyer wants to purchase special bread for the public schools this winter.";
        db.Tenders.Add(CreateTender(203, summary));
        await db.SaveChangesAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FakeProfileGenerator(invalidPaymentDescription: "The buyer wants to purchase special bread for schools")),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task GenerateRejectsRepetitivePaymentTemplatesWithoutPersisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender(204, "Supply of bread for school kitchens."));
        await db.SaveChangesAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FakeProfileGenerator(repetitivePayments: true)),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task GenerateSurfacesModelFailureAndSavesNothing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender(301, "Purchase of bread for school kitchens."));
        await db.SaveChangesAsync();

        var controller = new CustomersController(db);
        var response = await controller.Generate(
            new CustomerGenerationService(db, new FailingProfileGenerator()),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.CustomerPayments.CountAsync());
    }

    [Fact]
    public void PromptDefinesConciseSafeProfileOutputAndRequestedDistribution()
    {
        const string summary = "The buyer wants a new laboratory equipment base";
        var prompt = CustomerProfilePrompt.Build(summary, "ru", shouldMatchTender: true);

        Assert.Contains("without a customer ID", prompt);
        Assert.Contains("short, simple activity label", prompt);
        Assert.Contains("Payment for bread delivery, contract No 14", prompt);
        Assert.Contains("Do not quote, copy, or paraphrase long phrases", prompt);
        Assert.Contains("\"RequestedPreferredLanguageCode\":\"ru\"", prompt);
        Assert.Contains("\"MustMatchTender\":true", prompt);
        Assert.Contains(summary, prompt);
    }

    private static TenderEntity CreateTender(long id, string summary) => new()
    {
        Id = id,
        DisplayNo = id.ToString(),
        Source = "UZEX",
        SourceUrl = $"https://example.test/tenders/{id}",
        Name = summary,
        BuyerName = "Public buyer",
        StartDate = DateTime.Today,
        EndDate = DateTime.Today.AddDays(30),
        StartPrice = 240_000_000,
        CurrencyCode = "UZS",
        Summary = summary,
        RegionName = "Tashkent",
        DeliveryRegion = "Tashkent",
        DeliveryDistrict = "Yunusobod"
    };

    private static bool MeetsFinancingTerms(CustomerEntity customer, DateOnly today)
    {
        var risk = customer.RiskFeatures!;
        return risk.KycPassed
            && CustomerMapping.MonthsBetween(customer.CustomerSince, today) >= 12
            && risk.MaxOverdueDays12m == 0
            && risk.OverdueAmount == 0
            && risk.AvgMonthlyInflow6m >= 80_000_000
            && risk.PreApprovedLimit is > 0;
    }

    private sealed class FakeProfileGenerator(
        string? invalidCompanyName = null,
        string? invalidIndustryName = null,
        string? invalidPaymentDescription = null,
        bool repetitivePayments = false) : ICustomerProfileGenerator
    {
        public Task<CustomerProfileDraft> GenerateAsync(
            string tenderSummary,
            string languageCode,
            bool shouldMatchTender,
            CancellationToken ct)
        {
            var breadTender = tenderSummary.Contains("bread", StringComparison.OrdinalIgnoreCase);
            var payments = shouldMatchTender
                ? breadTender
                    ? new List<string>
                    {
                        "Payment for bread delivery, contract No 14",
                        "Kindergarten No 7: payment for bread, September",
                        "Purchase of flour, invoice 2231"
                    }
                    : new List<string>
                    {
                        "Payment for food supply, contract No 18",
                        "Regional hospital: food delivery invoice 28",
                        "Purchase of rice, invoice 304"
                    }
                : [];
            if (invalidPaymentDescription is not null && payments.Count > 0)
            {
                    payments[0] = invalidPaymentDescription;
            }

            if (repetitivePayments && payments.Count > 0)
            {
                    payments =
                    [
                        "Payment for bread supply, contract No 14",
                        "Payment for bread supply, contract No 18",
                        "Payment for bread supply, contract No 21"
                    ];
            }

            var draft = new CustomerProfileDraft
            {
                CompanyName = invalidCompanyName ?? (shouldMatchTender ? breadTender ? "Navqiron Supplies" : "Orzu Foods" : "Istiqlol Digital"),
                IndustryName = invalidIndustryName ?? (shouldMatchTender ? breadTender ? "Manufacture of bread" : "Wholesale of food products" : "Software development"),
                MatchesTender = shouldMatchTender,
                RecentPaymentDescriptions = payments
            };
            return Task.FromResult(draft);
        }
    }

    private sealed class FailingProfileGenerator : ICustomerProfileGenerator
    {
        public Task<CustomerProfileDraft> GenerateAsync(
            string tenderSummary,
            string languageCode,
            bool shouldMatchTender,
            CancellationToken ct) =>
            Task.FromException<CustomerProfileDraft>(new CustomerProfileGenerationException("Bedrock unavailable."));
    }
}
