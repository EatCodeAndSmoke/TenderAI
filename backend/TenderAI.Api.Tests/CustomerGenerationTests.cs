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
        Assert.InRange(saved.RiskFeatures.PreApprovedLimit!.Value, 2_000_000_000L, 60_000_000_000L);
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
            Assert.Equal(18, customers.Select(c => c.Industry!.IndustryCode).Distinct().Count());
            Assert.DoesNotContain(customers, customer => customer.CustomerId == "C-01" || customer.TaxCode == "300000001");
            Assert.Equal(60, await setup.CustomerAccounts.CountAsync());
        }
        finally
        {
            foreach (var db in contexts) await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task GenerationUsesDiverseActivitiesBeforeRepeating()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var generator = new CustomerGenerationService(db);
        var generated = new List<CustomerRecordDto>();
        for (var i = 0; i < 36; i++)
            generated.Add(Assert.Single(await generator.GenerateAsync(CancellationToken.None)));

        var codes = generated.Select(record => record.Customer!.Industry!.IndustryCode).ToArray();
        Assert.Equal(18, codes.Take(18).Distinct().Count());
        Assert.Equal(18, codes.Skip(18).Distinct().Count());
        Assert.All(generated, record =>
        {
            var incoming = record.RecentPayments.Where(payment => payment.IsIncoming).ToArray();
            Assert.Equal(2, incoming.Length);
            Assert.Single(record.RecentPayments, payment => !payment.IsIncoming);
            Assert.InRange(record.RiskFeatures!.PreApprovedLimit!.Value, 2_000_000_000L, 60_000_000_000L);
            Assert.Contains(CustomerGenerationService.Profiles, profile =>
                profile.Code == record.Customer!.Industry!.IndustryCode
                && profile.Name == record.Customer.Industry.Name
                && incoming.Any(payment => payment.PurposeDescription.Contains(profile.FirstSale, StringComparison.Ordinal))
                && incoming.Any(payment => payment.PurposeDescription.Contains(profile.SecondSale, StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void ProfileCatalogCoversTenderSpecificSalesAndWeatherEvidence()
    {
        var profiles = CustomerGenerationService.Profiles;
        var expectedSales = new[]
        {
            "straight-run gasoil", "gas condensate", "methyl tert-butyl ether", "monomethylaniline",
            "college building reconstruction", "school facade", "office building renovation",
            "hydroelectric power plant construction", "transmission line", "road reconstruction feasibility",
            "hydroelectric power plant project management", "housing project finance", "escrow",
            "air conditioners", "refrigerator", "water cooler", "school kitchen",
            "immunoglobulin", "carboplatin", "dexpanthenol", "mupirocin", "neonatal screening",
            "PCR diagnostic", "hepatitis", "tuberculosis diagnostic", "automatic weather stations",
            "solar radiation", "voltage measurement", "time and frequency", "radiofrequency",
            "audiometry", "connection duration", "gastroendoscope", "videocolonoscope",
            "bronchoscope", "hysteroscope", "arthroscope", "laparoscope", "electrocardiogram",
            "blood collection", "donor blood processing", "shredder", "press-compactor",
            "gas turbine engine", "bank deposit machines", "monoblock", "laptop", "video conference",
            "mobile crane", "multilift", "school student desks", "school bookcases",
            "financial management information system", "video surveillance", "drought-resistant garden",
            "gas generator maintenance"
        };
        foreach (var expected in expectedSales)
            Assert.Contains(profiles, profile =>
                profile.FirstSale.Contains(expected, StringComparison.OrdinalIgnoreCase)
                || profile.SecondSale.Contains(expected, StringComparison.OrdinalIgnoreCase));

        var weather = Assert.Single(profiles, profile => profile.FirstSale.Contains("automatic weather stations", StringComparison.Ordinal));
        Assert.Equal("46690", weather.Code);
        Assert.Contains("meteorological", weather.Name);
        Assert.Contains("weather", weather.SecondSale);
        Assert.DoesNotContain(profiles.Where(profile => profile.Code == "46510"),
            profile => profile.FirstSale.Contains("weather", StringComparison.OrdinalIgnoreCase)
                || profile.SecondSale.Contains("weather", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task CustomerUpdatePersistsEditedProfileRiskAndPaymentsWithoutChangingAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var controller = new CustomersController(db);
        var generated = Assert.Single(await new CustomerGenerationService(db).GenerateAsync(CancellationToken.None));
        var id = generated.Customer!.CustomerId;
        var originalAccounts = await db.CustomerAccounts.AsNoTracking().Where(a => a.CustomerId == id)
            .OrderBy(a => a.Currency).Select(a => new { a.Iban, a.Currency, a.Balance }).ToListAsync();
        db.ChangeTracker.Clear();

        generated.Customer.Name = "Edited business";
        generated.Customer.Industry = new IndustryDto { IndustryCode = "49410", Name = "Freight transport by road" };
        generated.Customer.Address!.RegionName = "Bukhara";
        generated.RiskFeatures!.KycPassed = false;
        generated.RiskFeatures.PreApprovedLimit = null;
        generated.OnRegister = true;
        generated.RecentPayments =
        [
            new PaymentDto { Date = new DateOnly(2026, 10, 1), IsIncoming = false, PurposeDescription = "Fuel purchase", Amount = null },
            new PaymentDto { Date = new DateOnly(2026, 10, 2), IsIncoming = true, PurposeDescription = "Freight services", Amount = 0 }
        ];

        var response = await controller.Update(id.ToLowerInvariant(), generated, CancellationToken.None);
        Assert.Equal("Edited business", response.Value!.Customer!.Name);
        db.ChangeTracker.Clear();
        var persisted = (await controller.Get(id, CancellationToken.None)).Value!;
        Assert.Equal("Edited business", persisted.Customer!.Name);
        Assert.Equal("49410", persisted.Customer.Industry!.IndustryCode);
        Assert.Equal("Bukhara", persisted.Customer.Address!.RegionName);
        Assert.False(persisted.RiskFeatures!.KycPassed);
        Assert.Null(persisted.RiskFeatures.PreApprovedLimit);
        Assert.True(persisted.OnRegister);
        Assert.Equal(2, persisted.RecentPayments.Count);
        Assert.Contains(persisted.RecentPayments, p => p.PurposeDescription == "Fuel purchase" && !p.IsIncoming && p.Amount is null);
        Assert.Contains(persisted.RecentPayments, p => p.PurposeDescription == "Freight services" && p.IsIncoming && p.Amount == 0);
        var accounts = await db.CustomerAccounts.AsNoTracking().Where(a => a.CustomerId == id)
            .OrderBy(a => a.Currency).Select(a => new { a.Iban, a.Currency, a.Balance }).ToListAsync();
        Assert.Equal(originalAccounts, accounts);
    }
}
