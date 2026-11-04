using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
                new CustomerGenerationService(db, new FakeProfileGenerator(nonMatchingWithPayments: true)),
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
        Assert.Equal(2, matched.Select(customer => customer.IndustryCode).Distinct().Count());
        Assert.All(persisted, customer => Assert.Equal(
            IndustryDescriptionFor(customer.IndustryCode),
            customer.IndustryName));
        Assert.Equal(3, persisted.Select(customer => NormalizeCompanyName(customer.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        Assert.All(matched, customer =>
        {
            var expectedProduct = customer.IndustryCode == "10710" ? "bread" : "food";
            var incomingPayments = customer.Payments.Where(payment => payment.IsIncoming).ToList();
            Assert.Equal(2, incomingPayments.Count);
            Assert.All(incomingPayments, payment =>
                Assert.Contains(expectedProduct, payment.PurposeDescription, StringComparison.OrdinalIgnoreCase));
        });

        var unmatched = Assert.Single(persisted, customer => customer.OnRegister);
        Assert.Empty(unmatched.Payments);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var financingEligible = matched.Where(customer => MeetsFinancingTerms(customer, today, 240_000_000)).ToList();
        var financingIneligible = matched.Where(customer => !MeetsFinancingTerms(customer, today, 240_000_000)).ToList();
        Assert.Single(financingEligible);
        Assert.True(financingEligible[0].RiskFeatures!.PreApprovedLimit is >= 192_000_000);
        var overdueCustomer = Assert.Single(financingIneligible);
        Assert.Equal(45, overdueCustomer.RiskFeatures!.MaxOverdueDays12m);
        Assert.True(overdueCustomer.RiskFeatures.OverdueAmount > 0);
    }

    [Fact]
    public async Task GenerateRetriesNamesThatConflictWithExistingCustomersAndGeneratedBatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(new CustomerEntity
        {
            CustomerId = "C-01",
            Name = "Shared Company LLC",
            TaxCode = "300000001",
            Type = "LLC",
            IndustryCode = "62010",
            IndustryName = "Computer programming activities",
            RegionName = "Tashkent",
            LanguageCode = "en",
            CustomerSince = new DateOnly(2020, 1, 1),
            RiskFeatures = new CustomerRiskFeaturesEntity { CustomerId = "C-01", KycPassed = true }
        });
        db.Tenders.AddRange(
            CreateTender(401, "The buyer is purchasing bread for public schools."),
            CreateTender(402, "Supply of food products to regional hospitals."));
        await db.SaveChangesAsync();

        var generator = new FakeProfileGenerator(duplicateNames: true);
        var controller = new CustomersController(db);
        var response = await controller.Generate(new CustomerGenerationService(db, generator), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var saved = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, saved.Count);
        Assert.True(generator.CallCount > 3);
        Assert.DoesNotContain(saved, customer =>
            NormalizeCompanyName(customer.Customer!.Name) == NormalizeCompanyName("Shared Company LLC"));
        Assert.Equal(4, await db.Customers.CountAsync());

        var allNames = await db.Customers.Select(customer => customer.Name).ToListAsync();
        Assert.Equal(allNames.Count, allNames.Select(NormalizeCompanyName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task GenerateFallsBackToUniqueNamesAfterBoundedRetries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(new CustomerEntity
        {
            CustomerId = "C-01",
            Name = "Shared Company LLC",
            TaxCode = "300000001",
            Type = "LLC",
            IndustryCode = "62010",
            IndustryName = "Computer programming activities",
            RegionName = "Tashkent",
            LanguageCode = "en",
            CustomerSince = new DateOnly(2020, 1, 1),
            RiskFeatures = new CustomerRiskFeaturesEntity { CustomerId = "C-01", KycPassed = true }
        });
        db.Tenders.Add(CreateTender(403, "The buyer is purchasing bread for public schools."));
        await db.SaveChangesAsync();

        var generator = new FakeProfileGenerator(alwaysDuplicateNames: true);
        var response = await new CustomersController(db)
            .Generate(new CustomerGenerationService(db, generator), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, generated.Count);
        Assert.True(generator.CallCount >= 5);
        Assert.Equal(4, await db.Customers.CountAsync());
        Assert.Equal(6, await db.CustomerPayments.CountAsync());
        var names = await db.Customers.Select(customer => customer.Name).ToListAsync();
        Assert.Equal(names.Count, names.Select(NormalizeCompanyName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ActivityDescriptionIsCanonicalForItsOkedCodeEvenWhenModelWordingDiffers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender(501, "Supply of bread for school kitchens."));
        await db.SaveChangesAsync();

        var generator = new FakeProfileGenerator(duplicateNames: true, variedBreadActivityLabels: true);
        var response = await new CustomersController(db)
            .Generate(new CustomerGenerationService(db, generator), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var saved = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();

        Assert.Equal(3, saved.Count);
        var matched = saved.Where(customer => !customer.OnRegister).ToList();
        Assert.Equal(2, matched.Count);
        Assert.All(matched, customer =>
        {
            var profile = customer.Customer!;
            var industry = profile.Industry!;
            Assert.Equal("10710", industry.IndustryCode);
            Assert.Equal("Manufacture of bread", industry.Name);
        });
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
    public async Task GenerateUsesOnlyOpenTendersForMatchingProfiles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var expiredBreadTender = CreateTender(601, "The buyer is purchasing bread for public schools.");
        expiredBreadTender.EndDate = DateTime.Today.AddDays(-1);
        db.Tenders.AddRange(
            expiredBreadTender,
            CreateTender(602, "Supply of food products to regional hospitals."));
        await db.SaveChangesAsync();

        var response = await new CustomersController(db)
            .Generate(new CustomerGenerationService(db, new FakeProfileGenerator()), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        var matchingProfiles = generated.Where(customer => !customer.OnRegister).ToList();
        Assert.Equal(2, matchingProfiles.Count);
        Assert.All(matchingProfiles, customer =>
        {
            Assert.Equal("46390", customer.Customer!.Industry!.IndustryCode);
            Assert.All(customer.RecentPayments.Where(payment => payment.IsIncoming), payment =>
                Assert.Contains("food", payment.PurposeDescription, StringComparison.OrdinalIgnoreCase));
        });

        var matches = new MatchingService(
            db,
            new ProductEvidenceEvaluator(),
            new FixedTimeProvider(new DateTimeOffset(
                DateTime.SpecifyKind(DateTime.Today.AddDays(1).AddHours(12), DateTimeKind.Unspecified),
                TimeSpan.FromHours(5)).ToUniversalTime()),
            NullLogger<MatchingService>.Instance);
        var matchEvents = new List<MatchRunEvent>();
        await foreach (var runEvent in matches.RunStreamingAsync(CancellationToken.None))
        {
            matchEvents.Add(runEvent);
        }

        Assert.Equal(2, matchEvents
            .Where(runEvent => runEvent.Type == "customer-completed")
            .SelectMany(runEvent => runEvent.Matches!)
            .Select(match => match.CustomerId)
            .Distinct()
            .Count());
    }

    [Fact]
    public async Task GenerateCreatesMedicalTenderCustomersWithRelevantPaymentsAndAnEightyPercentLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var tender = CreateTender(603, "The buyer is purchasing medical items for public hospitals.");
        tender.Name = "PQ-20-sonli qaror doirasida tibbiy buyumlarni xarid qilish";
        tender.StartPrice = 750_000_000;
        tender.Products[0].ProductName = "tibbiy buyumlar";
        tender.Products[0].CategoryName = "tibbiy buyumlar";
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();

        var response = await new CustomersController(db)
            .Generate(new CustomerGenerationService(db, new FakeProfileGenerator()), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        var matching = generated.Where(customer => !customer.OnRegister).ToList();
        Assert.Equal(2, matching.Count);
        Assert.All(matching, customer =>
        {
            Assert.Equal("46460", customer.Customer!.Industry!.IndustryCode);
            Assert.NotNull(customer.RiskFeatures);
            Assert.All(customer.RecentPayments.Where(payment => payment.IsIncoming), payment =>
                Assert.Contains("medical", payment.PurposeDescription, StringComparison.OrdinalIgnoreCase));
        });

        var loanEligible = Assert.Single(matching, customer =>
            customer.RiskFeatures!.OverdueAmount == 0
            && customer.RiskFeatures.MaxOverdueDays12m == 0);
        Assert.True(loanEligible.RiskFeatures!.PreApprovedLimit is >= 600_000_000);
    }

    [Fact]
    public async Task GenerateNormalizesCustomerIdsOutOfGeneratedNames()
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

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, generated.Count);
        Assert.All(generated, customer => Assert.DoesNotContain("C 99", customer.Customer!.Name));
        Assert.Equal(3, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task GenerateCanonicalizesActivityCopiedFromTenderInsteadOfFailing()
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

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, generated.Count);
        Assert.All(generated.Where(customer => !customer.OnRegister), customer =>
        {
            var profile = customer.Customer!;
            var industry = profile.Industry!;
            Assert.Equal("10710", industry.IndustryCode);
            Assert.Equal("Manufacture of bread", industry.Name);
        });
    }

    [Fact]
    public async Task GenerateReplacesPaymentTextCopiedFromTenderInsteadOfFailing()
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

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, generated.Count);
        Assert.All(generated.Where(customer => !customer.OnRegister).SelectMany(customer => customer.RecentPayments), payment =>
        {
            Assert.DoesNotContain("The buyer wants", payment.PurposeDescription, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(payment.PurposeDescription.Length, 8, 120);
        });
    }

    [Fact]
    public async Task GenerateReplacesRepetitivePaymentTemplatesInsteadOfFailing()
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

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var generated = Assert.IsAssignableFrom<IEnumerable<CustomerRecordDto>>(ok.Value).ToList();
        Assert.Equal(3, generated.Count);
        Assert.All(generated.Where(customer => !customer.OnRegister), customer =>
            Assert.Equal(3, customer.RecentPayments.Select(payment => payment.PurposeDescription).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
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
        var prompt = CustomerProfilePrompt.Build(
            summary,
            "ru",
            shouldMatchTender: true,
            ["Existing Company LLC"],
            ["Manufacture of bread"],
            ["Payment for flour"]);

        Assert.Contains("without a customer ID", prompt);
        Assert.Contains("short, simple activity label", prompt);
        Assert.Contains("Payment for bread delivery, contract No 14", prompt);
        Assert.Contains("Do not quote, copy, or paraphrase long phrases", prompt);
        Assert.Contains("\"RequestedPreferredLanguageCode\":\"ru\"", prompt);
        Assert.Contains("\"MustMatchTender\":true", prompt);
        Assert.Contains("Existing Company LLC", prompt);
        Assert.Contains("Manufacture of bread", prompt);
        Assert.Contains("Payment for flour", prompt);
        Assert.Contains(summary, prompt);
    }

    [Theory]
    [InlineData("offer", true, true)]
    [InlineData("match", true, false)]
    [InlineData("none", false, false)]
    public async Task SingleCustomerScenarioIsVerifiedAndAccountsPersist(string scenario, bool hasMatch, bool hasOffer)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender(101, "The buyer is purchasing bread for public schools."));
        await db.SaveChangesAsync();
        var matcher = new MatchingService(db, new ProductEvidenceEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        var generator = new CustomerGenerationService(db, new FakeProfileGenerator());

        var result = await generator.GenerateAsync(scenario, CancellationToken.None, matcher);
        Assert.Single(result);
        var customer = await db.Customers.Include(c => c.Accounts).SingleAsync();
        Assert.Equal(new[] { "EUR", "GEL", "USD" }, customer.Accounts.Select(a => a.Currency).Order().ToArray());
        var iban = Assert.Single(customer.Accounts.Select(a => a.Iban).Distinct());
        Assert.Matches(@"^GE\d{2}TB\d{16}$", iban);
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var character in rearranged)
            foreach (var digit in char.IsDigit(character) ? character.ToString() : (character - 'A' + 10).ToString())
                remainder = (remainder * 10 + digit - '0') % 97;
        Assert.Equal(1, remainder);

        var events = new List<MatchRunEvent>();
        await foreach (var item in matcher.RunStreamingAsync(CancellationToken.None, customer.CustomerId)) events.Add(item);
        var matches = Assert.Single(events, e => e.Type == "customer-completed").Matches!;
        Assert.Equal(hasMatch, matches.Count > 0);
        Assert.Equal(hasOffer, matches.Any(m => m.Offer is not null));
        var notifications = await new CustomersController(db).GetNotifications(customer.CustomerId, CancellationToken.None);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<NotificationDto>>(notifications.Value));
    }

    [Fact]
    public async Task FailedScenarioVerificationDoesNotSaveCustomerOrAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender(101, "The buyer is purchasing bread for public schools."));
        await db.SaveChangesAsync();
        var matcher = new MatchingService(db, new NoMatchesEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        var generator = new CustomerGenerationService(db, new FakeProfileGenerator());
        await Assert.ThrowsAsync<CustomerProfileGenerationException>(() => generator.GenerateAsync("offer", CancellationToken.None, matcher));
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.CustomerAccounts.ToListAsync());
    }

    private sealed class NoMatchesEvaluator : ITenderMatchEvaluator
    {
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(
            CustomerMatchEvidence customer, IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenderCandidateMatch>>([]);
    }

    private static TenderEntity CreateTender(long id, string summary)
    {
        var productName = summary.Contains("bread", StringComparison.OrdinalIgnoreCase)
            ? "bread"
            : summary.Contains("food", StringComparison.OrdinalIgnoreCase) ? "food products" : summary;
        return new TenderEntity
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
            EnglishTitle = "Tender supply",
            RegionName = "Tashkent",
            DeliveryRegion = "Tashkent",
            DeliveryDistrict = "Yunusobod",
            Products =
            [
                new TenderProductEntity
                {
                    OrderNum = 1,
                    ProductName = productName,
                    CategoryName = productName
                }
            ]
        };
    }

    private static bool MeetsFinancingTerms(CustomerEntity customer, DateOnly today, decimal tenderAmount)
    {
        var risk = customer.RiskFeatures!;
        return risk.KycPassed
            && CustomerMapping.MonthsBetween(customer.CustomerSince, today) >= 12
            && risk.MaxOverdueDays12m == 0
            && risk.OverdueAmount == 0
            && risk.AvgMonthlyInflow6m >= 80_000_000
            && risk.PreApprovedLimit is { } limit
            && limit >= tenderAmount * 0.8m;
    }

    private static string IndustryDescriptionFor(string code) => code switch
    {
        "10710" => "Manufacture of bread",
        "46390" => "Wholesale of food products",
        "46499" => "Wholesale of paper and stationery",
        "81210" => "General cleaning of buildings",
        "41000" => "Building construction and repair",
        "46460" => "Wholesale of pharmaceutical goods",
        "46510" => "Wholesale of computers and peripheral equipment",
        "62010" => "Computer programming activities",
        "46690" => "Wholesale of other machinery and equipment",
        "46610" => "Wholesale of agricultural machinery",
        "46470" => "Wholesale of furniture and lighting equipment",
        "46420" => "Wholesale of clothing and footwear",
        "47300" => "Retail sale of automotive fuel",
        _ => "Non-specialized wholesale trade"
    };

    private static string NormalizeCompanyName(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !word.Equals("LLC", StringComparison.OrdinalIgnoreCase))
            .Select(word => word.ToLowerInvariant()));

    private sealed class FakeProfileGenerator(
        string? invalidCompanyName = null,
        string? invalidIndustryName = null,
        string? invalidPaymentDescription = null,
        bool repetitivePayments = false,
        bool duplicateNames = false,
        bool variedBreadActivityLabels = false,
        bool alwaysDuplicateNames = false,
        bool nonMatchingWithPayments = false) : ICustomerProfileGenerator
    {
        private int callCount;
        public int CallCount => callCount;

        public Task<CustomerProfileDraft> GenerateAsync(
            string tenderSummary,
            string languageCode,
            bool shouldMatchTender,
            IReadOnlyCollection<string> excludedCompanyNames,
            IReadOnlyCollection<string> excludedIndustryNames,
            IReadOnlyCollection<string> excludedPaymentDescriptions,
            CancellationToken ct)
        {
            var breadTender = tenderSummary.Contains("bread", StringComparison.OrdinalIgnoreCase);
            var payments = shouldMatchTender || nonMatchingWithPayments
                ? breadTender
                    ? languageCode == "ru"
                        ? new List<string>
                        {
                            "Payment for bread and buns, cafe contract No 3",
                            "Kindergarten No 12: monthly bakery delivery",
                            "Purchase of flour and yeast, invoice 884"
                        }
                        : new List<string>
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

            var callNumber = Interlocked.Increment(ref callCount);
            var companyName = alwaysDuplicateNames
                ? "Shared Company"
                : duplicateNames
                ? callNumber <= 3
                    ? "Shared Company"
                    : languageCode switch
                    {
                        "en" => "Navqiron Bakery",
                        "ru" => "Orzu Foods",
                        _ => "Istiqlol Digital"
                    }
                : shouldMatchTender ? breadTender ? "Navqiron Supplies" : "Orzu Foods" : "Istiqlol Digital";
            var industryName = variedBreadActivityLabels && shouldMatchTender && breadTender
                ? languageCode == "ru" ? "Bread manufacturing" : "Manufacture of bread"
                : shouldMatchTender ? breadTender ? "Manufacture of bread" : "Wholesale of food products" : "Software development";
            var draft = new CustomerProfileDraft
            {
                CompanyName = invalidCompanyName ?? companyName,
                IndustryName = invalidIndustryName ?? industryName,
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
            IReadOnlyCollection<string> excludedCompanyNames,
            IReadOnlyCollection<string> excludedIndustryNames,
            IReadOnlyCollection<string> excludedPaymentDescriptions,
            CancellationToken ct) =>
            Task.FromException<CustomerProfileDraft>(new CustomerProfileGenerationException("Bedrock unavailable."));
    }

    private sealed class ProductEvidenceEvaluator : ITenderMatchEvaluator
    {
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(
            CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders,
            CancellationToken ct)
        {
            var payments = string.Join(' ', customer.IncomingPayments.Select(payment => payment.PurposeDescription));
            IReadOnlyList<TenderCandidateMatch> matches = tenders
                .Where(tender =>
                {
                    var productText = string.Join(' ', tender.Products.Select(product => product.ProductName));
                    var isFoodTender = productText.Contains("food", StringComparison.OrdinalIgnoreCase);
                    var activityMatches = isFoodTender
                        ? customer.IndustryCode == "46390"
                        : customer.IndustryCode == "10710"
                            && productText.Contains("bread", StringComparison.OrdinalIgnoreCase);
                    var paymentsMatch = isFoodTender
                        ? payments.Contains("food", StringComparison.OrdinalIgnoreCase)
                        : payments.Contains("bread", StringComparison.OrdinalIgnoreCase);
                    return activityMatches && paymentsMatch;
                })
                .Select(tender => new TenderCandidateMatch(
                    tender.TenderId,
                    "The activity and incoming-payment descriptions support the requested product."))
                .ToList();
            return Task.FromResult(matches);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
