using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderAI.Api.Controllers;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using TenderAI.Api.Services.Uzex;
using Xunit;

namespace TenderAI.Api.Tests;

public class TenderDiscoveryTests
{
    [Fact]
    public async Task DiscoveryStreamsStoredMatchesUpToRequestedCountAndPersistsFeedback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(Customer("C-1"), Customer("C-2"));
        db.Tenders.AddRange(Enumerable.Range(1, 4).Select(Tender));
        db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = "C-1", TenderId = 1, Value = "dislike" });
        db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = "C-1", TenderId = 2, Value = "like" });
        await db.SaveChangesAsync();

        var handler = new UzexHandler(0);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var importer = Importer(db, http);
        var matcher = Matcher(db);
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 2, importer, CancellationToken.None)) events.Add(item);
        var matches = events.Where(e => e.Type == "match").ToList();
        Assert.Equal(2, matches.Count);
        Assert.Equal(new long[] { 2, 3 }, matches.Select(m => m.Tender!.Id));
        Assert.Equal("like", matches[0].Match!.Feedback);
        Assert.Equal(2, events.Single(e => e.Type == "completed").Count);
        Assert.Equal(0, handler.ListCalls);
        var controller = new CustomersController(db);
        Assert.IsType<NoContentResult>(await controller.SetFeedback("C-1", 3, new TenderFeedbackRequest("dislike"), CancellationToken.None));
        var loaded = await controller.GetMatches("C-1", CancellationToken.None);
        Assert.Equal("2", Assert.Single(loaded.Value!).LotId);
        Assert.Equal("like", Assert.Single(loaded.Value!).Feedback);
        Assert.Equal(0, await db.TenderFeedback.CountAsync(f => f.CustomerId == "C-2"));
    }

    [Fact]
    public async Task DiscoveryImportsAdditionalTendersAndHonorsTenMatchLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.Add(Tender(1));
        await db.SaveChangesAsync();
        var handler = new UzexHandler(12);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 10, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(10, events.Count(e => e.Type == "match"));
        Assert.Equal(1, events.First(e => e.Type == "match").Tender!.Id);
        Assert.True(handler.ListCalls > 0);
        Assert.Equal(10, events.Single(e => e.Type == "completed").Count);
    }

    [Fact]
    public async Task DiscoveryBackfillsMissingEnglishTitleOnExistingMatchedTender()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var tender = Tender(1);
        tender.EnglishTitle = null;
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal("Bread supply", events.Single(e => e.Type == "match").Tender!.EnglishTitle);
        db.ChangeTracker.Clear();
        Assert.Equal("Bread supply", (await db.Tenders.SingleAsync()).EnglishTitle);
    }

    [Fact]
    public async Task DeleteHidesTenderAndPreventsReimport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.Add(Tender(1));
        db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = "C-1", TenderId = 1, Value = "like" });
        await db.SaveChangesAsync();
        var controller = new TendersController(db);
        using var initialHttp = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, initialHttp), CancellationToken.None)) { }
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.IsType<NoContentResult>(await controller.Delete(1, CancellationToken.None));
        Assert.Empty((await controller.GetAll(CancellationToken.None, true)).Value!);
        Assert.Empty(await db.TenderFeedback.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        var handler = new UzexHandler(2);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var imported = await Importer(db, http).LoadNewAsync(1, CancellationToken.None);
        Assert.Equal(2, Assert.Single(imported).Id);
    }

    [Fact]
    public async Task DiscoveryReportsWhenImportLimitIsReached()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(60)) { BaseAddress = new Uri("https://uzex.test/") };
        var matcher = new MatchingService(db, new NoMatchEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        var completed = Assert.Single(events, e => e.Type == "completed");
        Assert.True(completed.Limited);
        Assert.Equal(50, completed.Imported);
        Assert.Equal(0, completed.Count);
        Assert.Equal(50, await db.Tenders.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task DiscoveryRejectsCountsOutsideRange(int count)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in Matcher(db).DiscoverAsync("C-1", count, Importer(db, http), CancellationToken.None)) { }
        });
    }

    private static CustomerEntity Customer(string id) => new()
    {
        CustomerId = id, Name = "Bakery", TaxCode = "300000001", Type = "LLC", IndustryCode = "10710",
        IndustryName = "Bread", RegionName = "Tashkent", LanguageCode = "en", CustomerSince = new DateOnly(2020, 1, 1),
        RiskFeatures = new CustomerRiskFeaturesEntity { CustomerId = id, PreApprovedLimit = 2000000 },
        Payments = [new CustomerPaymentEntity { CustomerId = id, IsIncoming = true, Date = DateOnly.FromDateTime(DateTime.UtcNow), PurposeDescription = "Bread delivery", Amount = 100 }]
    };

    private static TenderEntity Tender(int id) => new()
    {
        Id = id, DisplayNo = id.ToString(), Source = "UZEX", SourceUrl = "https://uzex.test/",
        Name = "Bread supply", EnglishTitle = "Bread supply", Summary = "Supply bread.",
        SummaryVersion = TenderSummaryService.CurrentSummaryVersion, BuyerName = "Buyer",
        CurrencyCode = "UZS", StartPrice = 100, EndDate = DateTime.UtcNow.AddDays(10)
    };

    private static MatchingService Matcher(TenderAiDbContext db) =>
        new(db, new AllMatchEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);

    private static TenderImportService Importer(TenderAiDbContext db, HttpClient http) =>
        new(db, new UzexClient(http), new TenderSummaryService(new Summarizer(), NullLogger<TenderSummaryService>.Instance),
            Options.Create(new UzexOptions { ApiBaseUrl = "https://uzex.test/", LotUrl = "https://uzex.test/{0}" }),
            NullLogger<TenderImportService>.Instance);

    private sealed class AllMatchEvaluator : ITenderMatchEvaluator
    {
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenderCandidateMatch>>(tenders.Select(t =>
                new TenderCandidateMatch(t.TenderId, "Bread activity and incoming bread payments match.")).ToList());
    }

    private sealed class NoMatchEvaluator : ITenderMatchEvaluator
    {
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenderCandidateMatch>>([]);
    }

    private sealed class Summarizer : ITenderSummarizer
    {
        public Task<string> SummarizeAsync(TenderEntity tender, CancellationToken ct)
        {
            tender.EnglishTitle = "Bread supply";
            return Task.FromResult("Supply bread.");
        }
    }

    private sealed class UzexHandler(int count) : HttpMessageHandler
    {
        public int ListCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                ListCalls++;
                var rows = Enumerable.Range(1, count).Select(i => new
                {
                    id = i, total_count = count, display_no = $"LOT-{i}", name = "Bread supply",
                    end_date = DateTime.UtcNow.AddDays(10), cost = 100, currency_codeabc = "UZS"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(rows) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { name = "Bread supply", customer_name = "Buyer" })
            });
        }
    }
}
