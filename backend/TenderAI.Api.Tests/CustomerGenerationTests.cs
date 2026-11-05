using Microsoft.AspNetCore.Mvc;
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
    public async Task GeneratesOneCustomerWithoutTendersOrExternalServices()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using (var db = new TenderAiDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var response = await new CustomersController(db).Generate(new CustomerGenerationService(db), CancellationToken.None);
            var result = Assert.IsType<OkObjectResult>(response.Result);
            var generated = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(result.Value));
            Assert.Equal("C-01", generated.Customer!.CustomerId);
            Assert.Equal(3, generated.RecentPayments.Count);
            Assert.Equal(2, generated.RecentPayments.Count(p => p.IsIncoming));
            Assert.Empty(await db.Tenders.ToListAsync());
        }

        await using var verification = new TenderAiDbContext(options);
        var saved = await verification.Customers.Include(c => c.RiskFeatures).Include(c => c.Payments)
            .Include(c => c.Accounts).SingleAsync();
        Assert.NotEmpty(saved.Name);
        Assert.Matches(@"^\d{9}$", saved.TaxCode);
        Assert.NotEmpty(saved.IndustryCode);
        Assert.NotEmpty(saved.IndustryName);
        Assert.All(saved.Payments, payment => Assert.False(string.IsNullOrWhiteSpace(payment.PurposeDescription)));
        Assert.Equal(3, saved.Accounts.Count);
        Assert.Equal(new[] { "EUR", "GEL", "USD" }, saved.Accounts.Select(a => a.Currency).Order().ToArray());
        Assert.Single(saved.Accounts.Select(a => a.Iban).Distinct());
        Assert.NotNull(saved.RiskFeatures);
        Assert.NotNull(saved.RiskFeatures.PreApprovedLimit);
    }

    [Fact]
    public async Task RepeatedAndConcurrentGenerationUsesUniqueCustomerIdsNamesAndTaxCodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var setup = new TenderAiDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        setup.Customers.Add(new CustomerEntity { CustomerId = "C-01", Name = "Existing", TaxCode = "300000001" });
        await setup.SaveChangesAsync();

        var contexts = Enumerable.Range(0, 20).Select(_ => new TenderAiDbContext(options)).ToArray();
        try
        {
            var generated = await Task.WhenAll(contexts.Select(db => new CustomerGenerationService(db).GenerateAsync(CancellationToken.None)));
            var customers = generated.SelectMany(batch => batch).Select(record => record.Customer!).ToList();
            Assert.Equal(20, customers.Count);
            Assert.Equal(20, customers.Select(c => c.CustomerId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(20, customers.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(20, customers.Select(c => c.TaxCode).Distinct().Count());
            Assert.DoesNotContain(customers, customer => customer.CustomerId == "C-01" || customer.TaxCode == "300000001");
            Assert.Equal(60, await setup.CustomerAccounts.CountAsync());
        }
        finally
        {
            foreach (var db in contexts) await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManualCustomerCreationPersistsPaymentRiskAndAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var record = new CustomerRecordDto
        {
            Customer = new CustomerDto
            {
                CustomerId = "c-10", Name = "Manual Bakery", TaxCode = "123456789", Type = "LLC",
                Industry = new IndustryDto { IndustryCode = "10710", Name = "Manufacture of bread" },
                Address = new AddressDto { RegionName = "Tashkent", DistrictName = "Yunusobod" },
                LanguageCode = "en", CustomerSince = new DateOnly(2020, 1, 1)
            },
            RiskFeatures = new RiskFeaturesDto { AvgMonthlyInflow6m = 50_000_000, PreApprovedLimit = 200_000_000, KycPassed = true },
            RecentPayments = [new PaymentDto { Date = new DateOnly(2026, 10, 1), IsIncoming = true, PurposeDescription = "Bread delivery", Amount = 100 }]
        };
        var response = await new CustomersController(db).Add(record, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(response.Result);
        db.ChangeTracker.Clear();
        var saved = await db.Customers.Include(c => c.Payments).Include(c => c.RiskFeatures).Include(c => c.Accounts).SingleAsync();
        Assert.Equal("C-10", saved.CustomerId);
        Assert.Equal("Bread delivery", Assert.Single(saved.Payments).PurposeDescription);
        Assert.Equal(200_000_000, saved.RiskFeatures!.PreApprovedLimit);
        Assert.Equal(3, saved.Accounts.Count);
    }
}
