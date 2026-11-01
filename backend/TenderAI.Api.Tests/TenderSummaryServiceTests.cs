using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using TenderAI.Api.Services.Uzex;
using Xunit;

namespace TenderAI.Api.Tests;

public class TenderSummaryServiceTests
{
    [Fact]
    public void BedrockDefaultsUseFrankfurtAndTheEuNovaMicroProfile()
    {
        var options = new BedrockOptions();

        Assert.Equal("eu-central-1", options.Region);
        Assert.Equal("eu.amazon.nova-micro-v1:0", options.ModelId);
    }

    [Fact]
    public void PromptRequestsPlainEnglishAndIncludesOnlyTheStructuredSourceFacts()
    {
        var tender = CreateTender();
        tender.Name = "Uzbek tender title";
        tender.Description = "Uzbek tender description";
        tender.Requirements.Add("Uzbek requirement");
        tender.Products.Add(new TenderProductEntity
        {
            OrderNum = 1,
            ProductName = "Uzbek product",
            Quantity = 12,
            Unit = "pieces",
            Cost = 2400
        });

        var prompt = TenderSummaryPrompt.Build(tender);

        Assert.Contains("plain, simple English", prompt);
        Assert.Contains("do not translate it word for word", prompt);
        Assert.Contains("Do not guess", prompt);
        Assert.Contains("bid deadline", prompt);
        Assert.Contains("Uzbek tender description", prompt);
        Assert.Contains("Uzbek product", prompt);
        Assert.Contains("12", prompt);
        Assert.Contains("2400", prompt);
    }

    [Fact]
    public void ApiMappingExposesTheSummaryInsteadOfOriginalTenderText()
    {
        var tender = CreateTender();
        tender.Summary = "The buyer needs 12 items before December 1.";
        tender.SummaryError = null;

        var dto = tender.ToDto();

        Assert.Equal(tender.Summary, dto.Summary);
        Assert.Equal(tender.SourceUrl, dto.SourceUrl);
        Assert.Equal(tender.EndDate, dto.EndDate);
        Assert.Null(typeof(TenderDto).GetProperty("Name"));
        Assert.Null(typeof(TenderDto).GetProperty("BuyerName"));
    }

    [Fact]
    public async Task SummarizesTendersInParallelWithMaximumOfFiveRequests()
    {
        var active = 0;
        var maximum = 0;
        var summarizer = new DelegateSummarizer(async (tender, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximum, current);
            try
            {
                await Task.Delay(50, ct);
                return $"Simple summary {tender.Id}";
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var service = new TenderSummaryService(summarizer, NullLogger<TenderSummaryService>.Instance);
        var tenders = Enumerable.Range(1, 12).Select(id => CreateTender(id)).ToList();

        await service.SummarizeTendersAsync(tenders, CancellationToken.None);

        Assert.Equal(5, maximum);
        Assert.All(tenders, tender => Assert.StartsWith("Simple summary", tender.Summary));
    }

    [Fact]
    public async Task RecordsSummaryErrorsWithoutFallingBackToOriginalText()
    {
        var tender = CreateTender();
        var service = new TenderSummaryService(
            new DelegateSummarizer((_, _) => Task.FromException<string>(new InvalidOperationException("Bedrock unavailable"))),
            NullLogger<TenderSummaryService>.Instance);

        await service.SummarizeTendersAsync([tender], CancellationToken.None);

        Assert.Null(tender.Summary);
        Assert.Equal("Bedrock unavailable", tender.SummaryError);
        Assert.Equal("Uzbek tender title", tender.Name);
    }

    [Fact]
    public async Task RetriesMissingSummariesButDoesNotRegenerateCompletedSummaries()
    {
        await using var db = await CreateDatabaseAsync();
        var missing = CreateTender(1);
        var completed = CreateTender(2);
        completed.Summary = "Already stored";
        var failed = CreateTender(3);
        failed.SummaryError = "Previous Bedrock failure";
        db.Tenders.AddRange(missing, completed, failed);
        await db.SaveChangesAsync();

        var summarizedIds = new ConcurrentBag<long>();
        var service = new TenderSummaryService(
            new DelegateSummarizer((tender, _) =>
            {
                summarizedIds.Add(tender.Id);
                return Task.FromResult($"Summary {tender.Id}");
            }),
            NullLogger<TenderSummaryService>.Instance);

        Assert.Equal(2, await service.SummarizeMissingTendersAsync(db, CancellationToken.None));
        Assert.Equal(new long[] { 1, 3 }, summarizedIds.Order());
        Assert.Equal("Summary 1", missing.Summary);
        Assert.Equal("Already stored", completed.Summary);
        Assert.Equal("Summary 3", failed.Summary);
        Assert.Null(failed.SummaryError);
        Assert.Equal(0, await service.SummarizeMissingTendersAsync(db, CancellationToken.None));
        Assert.Equal(2, summarizedIds.Count);
    }

    [Fact]
    public async Task ImportCapsRequestsAtFiveAndSummarizesEachImportedTender()
    {
        await using var db = await CreateDatabaseAsync();
        var handler = new TenderApiHandler(7);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var summarizer = new DelegateSummarizer((tender, _) => Task.FromResult($"Summary {tender.Id}"));
        var summaryService = new TenderSummaryService(summarizer, NullLogger<TenderSummaryService>.Instance);
        var importer = new TenderImportService(
            db,
            new UzexClient(http),
            summaryService,
            Options.Create(new UzexOptions
            {
                ApiBaseUrl = "https://uzex.test/",
                LotUrl = "https://uzex.test/lot/{0}",
                TimeoutSeconds = 30
            }),
            NullLogger<TenderImportService>.Instance);

        var loaded = await importer.LoadNewAsync(20, CancellationToken.None);

        Assert.Equal(5, loaded.Count);
        Assert.Equal(5, handler.DetailsRequested);
        Assert.Equal(5, await db.Tenders.CountAsync());
        Assert.All(loaded, tender => Assert.Equal($"Summary {tender.Id}", tender.Summary));
    }

    private static TenderEntity CreateTender(long id = 1) => new()
    {
        Id = id,
        DisplayNo = $"LOT-{id}",
        Source = "UZEX",
        SourceUrl = $"https://example.test/{id}",
        Name = "Uzbek tender title",
        BuyerName = "Uzbek buyer",
        CurrencyCode = "UZS",
        StartPrice = 2400,
        EndDate = new DateTime(2026, 12, 1)
    };

    private static async Task<TenderAiDbContext> CreateDatabaseAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var current = Volatile.Read(ref maximum);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private sealed class DelegateSummarizer(
        Func<TenderEntity, CancellationToken, Task<string>> summarize) : ITenderSummarizer
    {
        public Task<string> SummarizeAsync(TenderEntity tender, CancellationToken ct) => summarize(tender, ct);
    }

    private sealed class TenderApiHandler(int totalCount) : HttpMessageHandler
    {
        private int _detailsRequested;

        public int DetailsRequested => Volatile.Read(ref _detailsRequested);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                var rows = Enumerable.Range(1, totalCount)
                    .Select(id => new
                    {
                        id,
                        display_no = $"LOT-{id}",
                        total_count = totalCount,
                        name = $"Tender {id}",
                        end_date = "2026-12-01T10:00:00",
                        cost = 2400,
                        currency_codeabc = "UZS",
                        seller_name = "Buyer"
                    });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(rows)
                });
            }

            Interlocked.Increment(ref _detailsRequested);
            var idText = request.RequestUri!.Segments[^2].TrimEnd('/');
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = long.Parse(idText),
                    name = $"Tender {idText}",
                    customer_name = "Buyer"
                })
            });
        }
    }
}

public class DatabaseSchemaTests
{
    [Fact]
    public void AddsSummaryColumnsWithoutRemovingExistingTenderData()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "Tenders" (
                    "Id" INTEGER PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "TranslationError" TEXT NULL
                );
                INSERT INTO "Tenders" ("Id", "Name", "TranslationError")
                VALUES (42, 'Existing tender', 'Legacy translation error');
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new TenderAiDbContext(options);

        DatabaseSchema.AddMissingColumns(db);
        DatabaseSchema.AddMissingColumns(db);

        using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT "Id", "Name", "TranslationError", "Summary", "SummaryError"
            FROM "Tenders"
            WHERE "Id" = 42;
            """;
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(42L, reader.GetInt64(0));
        Assert.Equal("Existing tender", reader.GetString(1));
        Assert.Equal("Legacy translation error", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));
    }
}
