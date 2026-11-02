using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

public class MatchingServiceTests
{
    [Fact]
    public async Task RunSendsOneCustomerAndAllOpenTendersAndPersistsReturnedMatches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Notifications\" DROP COLUMN \"WhyText\"");
        DatabaseSchema.AddMissingColumns(db);

        var customer = CreateCustomer("C-01", preApprovedLimit: 80_000_000);
        customer.Payments.Add(new CustomerPaymentEntity
        {
            CustomerId = customer.CustomerId,
            Date = new DateOnly(2026, 10, 14),
            IsIncoming = false,
            PurposeDescription = "Purchase of flour",
            Amount = 1_000_000
        });
        customer.Payments.Add(new CustomerPaymentEntity
        {
            CustomerId = customer.CustomerId,
            Date = new DateOnly(2025, 10, 15),
            IsIncoming = true,
            PurposeDescription = "Old bread sale",
            Amount = 500_000
        });
        db.Customers.Add(customer);
        var tender = CreateTender(name: "PQ-20-sonli qaror doirasida tibbiy buyumlarni xarid qilish");
        tender.Summary = "Purchase of bread for school kitchens";
        db.Tenders.Add(tender);
        var expiredTender = CreateTender(511681, "Expired bread supply");
        expiredTender.EndDate = new DateTime(2026, 10, 30);
        db.Tenders.Add(expiredTender);
        await db.SaveChangesAsync();

        var evaluator = new FakeMatchEvaluator(
            [new TenderCandidateMatch("511680", "OKED 10710 and incoming bread payments support this tender.")]);
        var events = await CollectEvents(CreateService(db, evaluator));

        var result = Assert.Single(events, runEvent => runEvent.Type == "customer-completed");
        var match = Assert.Single(result.Matches!);
        Assert.Equal("C-01", match.CustomerId);
        Assert.Equal("511680", match.LotId);
        Assert.Equal("Purchase of bread for school kitchens", match.TenderTitle);
        Assert.Equal(80_000_000L, match.Offer?.Financing);
        Assert.Equal(3_000_000L, match.Offer?.Guarantee);
        Assert.Equal(80_000_000L, match.Offer?.PreApprovedLimit);
        Assert.Contains("at least 80%", match.LoanDecisionReason);
        Assert.Equal(1, evaluator.Calls);
        Assert.Equal("C-01", evaluator.LastCustomer?.CustomerId);
        Assert.Equal("10710", evaluator.LastCustomer?.IndustryCode);
        Assert.Equal("Manufacture of bread", evaluator.LastCustomer?.IndustryName);
        Assert.Equal("Bread delivery payment", Assert.Single(evaluator.LastCustomer!.IncomingPayments).PurposeDescription);
        Assert.Equal(new[] { "511680" }, evaluator.LastTenders!.Select(tender => tender.TenderId));
        Assert.Equal("Purchase of bread for school kitchens", await db.Notifications.Select(notification => notification.Title).SingleAsync());
        Assert.Equal(match.MatchReason, await db.Notifications.Select(notification => notification.WhyText).SingleAsync());
    }

    [Fact]
    public async Task RunOnlyOffersLoanWhenLimitCoversEightyPercentAndNoArrearsWereRecorded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var eligible = CreateCustomer("C-01", preApprovedLimit: 80_000_000);
        var insufficientLimit = CreateCustomer("C-02", preApprovedLimit: 79_999_999);
        var overdueBalance = CreateCustomer("C-03", preApprovedLimit: 100_000_000, overdueAmount: 1);
        var overdueHistory = CreateCustomer("C-04", preApprovedLimit: 100_000_000, overdueDays: 1);
        db.Customers.AddRange(eligible, insufficientLimit, overdueBalance, overdueHistory);
        db.Tenders.Add(CreateTender());
        await db.SaveChangesAsync();

        var evaluator = new FakeMatchEvaluator(
            [new TenderCandidateMatch("511680", "The activity and incoming payments support the requested bread.")]);
        var events = await CollectEvents(CreateService(db, evaluator));
        var results = events.Where(runEvent => runEvent.Type == "customer-completed").ToList();
        var matches = results.SelectMany(runEvent => runEvent.Matches!).ToList();

        Assert.Equal(4, evaluator.Calls);
        Assert.Single(matches, match => match.CustomerId == "C-01" && match.Offer is not null);
        Assert.Single(matches, match => match.CustomerId == "C-02" && match.Offer is null
            && match.LoanDecisionReason.Contains("below 80%", StringComparison.Ordinal));
        Assert.Single(matches, match => match.CustomerId == "C-03" && match.Offer is null
            && match.LoanDecisionReason.Contains("arrears", StringComparison.Ordinal));
        Assert.Single(matches, match => match.CustomerId == "C-04" && match.Offer is null
            && match.LoanDecisionReason.Contains("arrears", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunKeepsFailedCustomersPreviousResultsAndContinuesStreamingOtherCustomers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var failedCustomer = CreateCustomer("C-01", 20_000_000);
        var completedCustomer = CreateCustomer("C-02", 20_000_000);
        db.Customers.AddRange(failedCustomer, completedCustomer);
        db.Tenders.Add(CreateTender());
        db.Notifications.Add(new NotificationEntity
        {
            Id = "N-OLD",
            CustomerId = failedCustomer.CustomerId,
            LotId = "old-lot",
            CreatedAt = DateTime.UtcNow,
            Title = "Existing match",
            Body = "Existing tender notice",
            Buyer = "Existing buyer",
            SourceUrl = "https://uzex.test/lot/old-lot",
            Region = "Tashkent",
            StartPrice = 1_000_000,
            CurrencyCode = "UZS",
            Deadline = new DateOnly(2026, 11, 30),
            MatchReason = "Existing match reason",
            LoanDecisionReason = "Existing loan decision"
        });
        await db.SaveChangesAsync();

        var evaluator = new FakeMatchEvaluator(
            [new TenderCandidateMatch("511680", "OKED 10710 and incoming bread payments support this tender.")],
            failingCustomerId: "C-01");
        var events = await CollectEvents(CreateService(db, evaluator));

        Assert.Contains(events, runEvent => runEvent.Type == "customer-error" && runEvent.CustomerId == "C-01");
        var completed = Assert.Single(events, runEvent => runEvent.Type == "customer-completed");
        Assert.Equal("C-02", completed.CustomerId);
        Assert.Equal(1, completed.FailedCustomers);
        Assert.Contains(events, runEvent => runEvent.Type == "completed" && runEvent.FailedCustomers == 1);
        Assert.Equal("Existing match", await db.Notifications
            .Where(notification => notification.CustomerId == "C-01")
            .Select(notification => notification.Title)
            .SingleAsync());
        Assert.Equal("C-02", await db.Notifications
            .Where(notification => notification.CustomerId == "C-02")
            .Select(notification => notification.CustomerId)
            .SingleAsync());
    }

    [Fact]
    public async Task RunContinuesWhenOneCustomersResultCannotBeSaved()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(
            CreateCustomer("C-01", preApprovedLimit: 80_000_000),
            CreateCustomer("C-02", preApprovedLimit: 80_000_000));
        db.Tenders.Add(CreateTender());
        db.Notifications.Add(new NotificationEntity
        {
            Id = "N-OLD",
            CustomerId = "C-01",
            LotId = "old-lot",
            CreatedAt = DateTime.UtcNow,
            Title = "Previous result",
            Body = "Previous tender notice",
            Buyer = "Existing buyer",
            SourceUrl = "https://uzex.test/lot/old-lot",
            Region = "Tashkent",
            StartPrice = 1_000_000,
            CurrencyCode = "UZS",
            Deadline = new DateOnly(2026, 11, 30),
            MatchReason = "Previous match reason",
            LoanDecisionReason = "Previous loan decision"
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_customer_one_notification
            BEFORE INSERT ON Notifications
            WHEN NEW.CustomerId = 'C-01'
            BEGIN
                SELECT RAISE(FAIL, 'test-only notification failure');
            END;
            """);

        var evaluator = new FakeMatchEvaluator(
            [new TenderCandidateMatch("511680", "OKED 10710 and incoming bread payments support this tender.")]);
        var events = await CollectEvents(CreateService(db, evaluator));

        Assert.Contains(events, runEvent => runEvent.Type == "customer-error"
            && runEvent.CustomerId == "C-01"
            && runEvent.Error!.Contains("could not be saved", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Type == "customer-completed"
            && runEvent.CustomerId == "C-02");
        Assert.Contains(events, runEvent => runEvent.Type == "completed" && runEvent.FailedCustomers == 1);
        Assert.Equal("Previous result", await db.Notifications
            .Where(notification => notification.CustomerId == "C-01")
            .Select(notification => notification.Title)
            .SingleAsync());
        Assert.Equal("C-02", await db.Notifications
            .Where(notification => notification.CustomerId == "C-02")
            .Select(notification => notification.CustomerId)
            .SingleAsync());
    }

    [Fact]
    public async Task StreamingEndpointFlushesCustomerResultsAsServerSentEvents()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(CreateCustomer("C-01", preApprovedLimit: 80_000_000));
        db.Tenders.Add(CreateTender());
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var controller = new MatchesController(
            db,
            CreateService(db, new FakeMatchEvaluator(
                [new TenderCandidateMatch("511680", "OKED 10710 and incoming bread payments support this tender.")])),
            NullLogger<MatchesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        await controller.RunStreaming(CancellationToken.None);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        responseBody.Position = 0;
        var body = await new StreamReader(responseBody, Encoding.UTF8).ReadToEndAsync();
        Assert.Contains("\"type\":\"started\"", body);
        Assert.Contains("\"type\":\"customer-completed\"", body);
        Assert.Contains("\"lotId\":\"511680\"", body);
        Assert.Contains("\"type\":\"completed\"", body);
    }

    [Fact]
    public void PromptIncludesCustomerPaymentsLocationAndTenderProductsWithoutFinancialRiskData()
    {
        var prompt = TenderMatchPrompt.Build(
            new CustomerMatchEvidence(
                "C-01",
                "10710",
                "Manufacture of bread",
                "Tashkent",
                "Yunusobod",
                [new IncomingPaymentEvidence(new DateOnly(2026, 10, 15), "Bread supply payment")]),
            [
                new TenderMatchEvidence(
                    "511680",
                    "School bread supply",
                    "Tashkent",
                    "Yunusobod",
                    [new TenderProductEvidence("10.71", "Bread", "Fresh bread", null, null)])
            ]);

        Assert.Contains("OKED code", prompt);
        Assert.Contains("incoming payment descriptions", prompt);
        Assert.Contains("location is optional", prompt);
        Assert.Contains("up to 45 words", prompt);
        Assert.Contains("immediately follow it with its English translation", prompt);
        Assert.Contains("C-01", prompt);
        Assert.Contains("Bread supply payment", prompt);
        Assert.Contains("511680", prompt);
        Assert.Contains("Fresh bread", prompt);
        Assert.DoesNotContain("preApprovedLimit", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overdueAmount", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotificationDtosUseTheStoredEnglishSummaryAsTheTenderTitle()
    {
        var notification = new NotificationEntity
        {
            Id = "N-123",
            CustomerId = "C-01",
            LotId = "511680",
            CreatedAt = new DateTime(2026, 11, 2),
            Title = "PQ-20-sonli qaror doirasida tibbiy buyumlarni xarid qilish",
            Body = "Purchase of medical items",
            Buyer = "Health department",
            SourceUrl = "https://example.test/tender/511680",
            Region = "Tashkent",
            StartPrice = 100_000_000,
            CurrencyCode = "UZS",
            Deadline = new DateOnly(2026, 11, 30),
            Requirements = [],
            MatchReason = "OKED 46460 and medical-supply payments support this match.",
            LoanDecisionReason = "No pre-approved limit is on record.",
            WhyText = "OKED 46460 and medical-supply payments support this match."
        };

        Assert.Equal("Purchase of medical items", notification.ToDto("Purchase of medical items").Title);
        Assert.Equal("Purchase of medical items", notification
            .ToMatchDto("Medical supplier", "Purchase of medical items")
            .TenderTitle);
    }

    private static async Task<List<MatchRunEvent>> CollectEvents(MatchingService service)
    {
        var events = new List<MatchRunEvent>();
        await foreach (var runEvent in service.RunStreamingAsync(CancellationToken.None))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static MatchingService CreateService(TenderAiDbContext db, ITenderMatchEvaluator evaluator) =>
        new(
            db,
            evaluator,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<MatchingService>.Instance);

    private static CustomerEntity CreateCustomer(
        string customerId,
        long? preApprovedLimit,
        long overdueAmount = 0,
        int overdueDays = 0) =>
        new()
        {
            CustomerId = customerId,
            Name = $"Bakery {customerId}",
            TaxCode = customerId == "C-01" ? "301234567" : $"30{customerId[^1]}234567",
            Type = "LLC",
            IndustryCode = "10710",
            IndustryName = "Manufacture of bread",
            RegionName = "Tashkent",
            DistrictName = "Yunusobod",
            LanguageCode = "en",
            RegistrationDate = new DateOnly(2019, 3, 4),
            CustomerSince = new DateOnly(2020, 1, 1),
            RiskFeatures = new CustomerRiskFeaturesEntity
            {
                CustomerId = customerId,
                AvgMonthlyInflow6m = 120_000_000,
                OverdueAmount = overdueAmount,
                MaxOverdueDays12m = overdueDays,
                PreApprovedLimit = preApprovedLimit,
                KycPassed = true
            },
            Payments =
            [
                new CustomerPaymentEntity
                {
                    CustomerId = customerId,
                    Date = new DateOnly(2026, 10, 15),
                    IsIncoming = true,
                    PurposeDescription = "Bread delivery payment",
                    Amount = 2_000_000
                }
            ]
        };

    private static TenderEntity CreateTender(long id = 511680, string name = "Bread supply") =>
        new()
        {
            Id = id,
            DisplayNo = id.ToString(CultureInfo.InvariantCulture),
            Source = "UZEX",
            SourceUrl = $"https://etender.uzex.uz/lot/{id}",
            Name = name,
            BuyerName = "School department",
            StartDate = new DateTime(2026, 11, 1),
            EndDate = new DateTime(2026, 11, 30),
            StartPrice = 100_000_000,
            CurrencyCode = "UZS",
            DepositPercent = 3,
            BankGuaranteeAccepted = true,
            Products =
            [
                new TenderProductEntity
                {
                    TenderId = id,
                    OrderNum = 1,
                    ProductCode = "10.71",
                    CategoryName = "Bread",
                    ProductName = "Fresh bread",
                    Quantity = 1000,
                    Price = 100_000,
                    Cost = 100_000_000
                }
            ]
        };

    private sealed class FakeMatchEvaluator(
        IReadOnlyList<TenderCandidateMatch> matches,
        string? failingCustomerId = null) : ITenderMatchEvaluator
    {
        public int Calls { get; private set; }
        public CustomerMatchEvidence? LastCustomer { get; private set; }
        public IReadOnlyCollection<TenderMatchEvidence>? LastTenders { get; private set; }

        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(
            CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders,
            CancellationToken ct)
        {
            Calls++;
            LastCustomer = customer;
            LastTenders = tenders;
            if (customer.CustomerId == failingCustomerId)
            {
                throw new TenderMatchEvaluationException("Malformed model response.");
            }

            var availableIds = tenders.Select(tender => tender.TenderId).ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<TenderCandidateMatch>>(
                matches.Where(match => availableIds.Contains(match.TenderId)).ToList());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
