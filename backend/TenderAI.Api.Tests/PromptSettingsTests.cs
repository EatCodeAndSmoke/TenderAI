using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderAI.Api.Controllers;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using Xunit;

namespace TenderAI.Api.Tests;

public class PromptSettingsTests
{
    [Fact]
    public void MatchingPromptsRequireSameSalesCategoryAndRejectCropServicesForDiesel()
    {
        Assert.Contains("same specific product or service category", TenderMatchPrompt.Instructions);
        Assert.Contains("incoming payment", TenderMatchPrompt.Instructions);
        Assert.Contains("Crop-production support", TenderMatchPrompt.Instructions);
        Assert.Contains("NOT a match", TenderMatchPrompt.Instructions);
        Assert.Contains("bread-supply tender", TenderMatchPrompt.Instructions);
        Assert.Contains("harvesting services", TenderMatchPrompt.Instructions);
        Assert.Contains("same specific category", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("crop-production support", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("diesel-fuel", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("untrusted data", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("automatic weather station", TenderMatchPrompt.Instructions);
        Assert.Contains("Computer sales alone do NOT support an automatic weather station tender", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("software developer with payments for software systems is NOT a match", TenderMatchPrompt.Instructions);
        Assert.Contains("Software development and payments for software systems do NOT support purchasing", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("A company that also sells the specific equipment may match", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("Score conservatively after verifying the direct-category match", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("Do not inflate a score to reach the return", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("120 words and 1200 characters", TenderMatchPrompt.Instructions);
        Assert.Contains("120", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("\"matchScore\"", TenderMatchPrompt.Instructions);
        Assert.Contains("\"matchScore\"", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("not measured", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("86-100", TenderMatchPrompt.Instructions);
        Assert.Contains("75-85", TenderMatchPrompt.Instructions);
        Assert.Contains("86-100", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("75-85", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("If earlier guidance specifies different score categories or ranges, use these ranges instead", TenderMatchPrompt.RequiredInstructions);
        Assert.Contains("omit tenders scored below 75", TenderMatchPrompt.RequiredInstructions);
    }

    [Fact]
    public async Task MatchingRequestIncludesConservativeRulesWithCustomGuidanceAndSoftwareEquipmentEvidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
        await db.Database.EnsureCreatedAsync();
        var controller = new PromptsController(db);
        await controller.Update("matching", new("CUSTOM MATCHING GUIDANCE", 0), CancellationToken.None);
        using var bedrock = new CapturingBedrockClient();
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, Options.Create(new BedrockOptions { ModelId = "test" }),
            services.GetRequiredService<PromptSettings>(), NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var customer = new CustomerMatchEvidence("C-42", "62010", "Software development", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Software system development")]);
        var tenders = new[] { new TenderMatchEvidence("42", "Purchase a variable current voltage standard", null, null,
            [new TenderProductEvidence(null, null, "Metrology equipment", "Installation and training included", null)]) };

        await evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None);

        var prompt = Assert.Single(bedrock.Prompts);
        Assert.Contains("CUSTOM MATCHING GUIDANCE", prompt);
        Assert.Contains(TenderMatchPrompt.RequiredInstructions, prompt);
        Assert.Contains("Score conservatively", prompt);
        Assert.Contains("software systems do NOT support purchasing a variable current", prompt);
        Assert.Contains("Software system development", prompt);
        Assert.Contains("Purchase a variable current voltage standard", prompt);
        Assert.Contains("Installation and training included", prompt);
    }

    [Fact]
    public async Task DefaultsValidationPersistenceConflictAndRestoreWorkAcrossScopes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var firstScope = services.CreateAsyncScope();
        var db = firstScope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
        await db.Database.EnsureCreatedAsync();
        var controller = new PromptsController(db);

        var defaults = (await controller.Get(CancellationToken.None)).Value!;
        Assert.Equal(2, defaults.Count);
        Assert.Equal(TenderMatchPrompt.Instructions, defaults.Single(p => p.Key == "matching").Instructions);
        Assert.Equal(TenderSummaryPrompt.Instructions, defaults.Single(p => p.Key == "summarization").Instructions);
        Assert.All(defaults, prompt => Assert.Equal(0, prompt.Revision));

        Assert.IsType<BadRequestObjectResult>((await controller.Update("matching", new("  ", 0), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Update("matching", new(new string('x', 20001), 0), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Update("unknown", new("text", 0), CancellationToken.None)).Result);
        var saved = (await controller.Update("matching", new("  Custom guidance  ", 0), CancellationToken.None)).Value!;
        Assert.Equal("Custom guidance", saved.Instructions);
        Assert.Equal(1, saved.Revision);

        await using var secondScope = services.CreateAsyncScope();
        var otherController = new PromptsController(secondScope.ServiceProvider.GetRequiredService<TenderAiDbContext>());
        var reloaded = (await otherController.Get(CancellationToken.None)).Value!.Single(p => p.Key == "matching");
        Assert.Equal(saved.Instructions, reloaded.Instructions);
        var changed = (await otherController.Update("matching", new("Changed elsewhere", reloaded.Revision), CancellationToken.None)).Value!;
        Assert.Equal(2, changed.Revision);
        Assert.IsType<ConflictObjectResult>((await controller.Update("matching", new("Stale edit", 1), CancellationToken.None)).Result);
        Assert.Equal("Changed elsewhere", await services.GetRequiredService<PromptSettings>().GetInstructionsAsync("matching", CancellationToken.None));

        var restored = (await otherController.Update("matching", new(TenderMatchPrompt.Instructions, 2), CancellationToken.None)).Value!;
        Assert.Equal(3, restored.Revision);
        Assert.Equal(TenderMatchPrompt.Instructions, await services.GetRequiredService<PromptSettings>().GetInstructionsAsync("matching", CancellationToken.None));
        Assert.Null((await secondScope.ServiceProvider.GetRequiredService<TenderAiDbContext>()
            .PromptSettings.AsNoTracking().SingleAsync(p => p.Key == "matching")).Instructions);
    }

    [Fact]
    public async Task AddsPromptTableToExistingDatabaseWithoutLosingData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Tenders.Add(CreateTender());
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"PromptSettings\"");

        DatabaseSchema.AddMissingTables(db);
        DatabaseSchema.AddMissingTables(db);

        Assert.Equal("Bread supply", (await db.Tenders.AsNoTracking().SingleAsync()).Name);
        Assert.Empty(await db.PromptSettings.ToListAsync());
        Assert.Equal(TenderSummaryPrompt.Instructions,
            await services.GetRequiredService<PromptSettings>().GetInstructionsAsync("summarization", CancellationToken.None));
    }

    [Fact]
    public async Task BedrockRequestsUseLatestSavedInstructionsAndRetainContractsAndData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
        await db.Database.EnsureCreatedAsync();
        var controller = new PromptsController(db);
        using var bedrock = new CapturingBedrockClient();
        var prompts = services.GetRequiredService<PromptSettings>();
        var options = Options.Create(new BedrockOptions { ModelId = "test" });
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, options, prompts, NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var summarizer = new BedrockTenderSummarizer(bedrock, options, prompts);
        var customer = new CustomerMatchEvidence("C-42", "10710", "Bakery", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Bread payment")]);
        var evidence = new[] { new TenderMatchEvidence("42", "Bread supply", null, null,
            [new TenderProductEvidence(null, null, "Bread", null, null)]) };
        var tender = CreateTender();

        await evaluator.EvaluateCustomerAsync(customer, evidence, CancellationToken.None);
        await summarizer.SummarizeAsync(tender, CancellationToken.None);
        Assert.Contains(TenderMatchPrompt.Instructions, bedrock.Prompts[0]);
        Assert.Contains(TenderSummaryPrompt.Instructions, bedrock.Prompts[1]);

        await controller.Update("matching", new("CUSTOM MATCHING GUIDANCE", 0), CancellationToken.None);
        await controller.Update("summarization", new("CUSTOM SUMMARY GUIDANCE", 0), CancellationToken.None);
        await evaluator.EvaluateCustomerAsync(customer, evidence, CancellationToken.None);
        Assert.Equal("Fresh bread", tender.EnglishTitle);
        Assert.Equal("Fresh bread for schools.", await summarizer.SummarizeAsync(tender, CancellationToken.None));

        Assert.Contains("CUSTOM MATCHING GUIDANCE", bedrock.Prompts[2]);
        Assert.Contains(TenderMatchPrompt.RequiredInstructions, bedrock.Prompts[2]);
        Assert.Contains("If earlier guidance calls for a shorter or less detailed explanation", bedrock.Prompts[2]);
        Assert.Contains("Bread payment", bedrock.Prompts[2]);
        Assert.Contains("\"TenderId\":\"42\"", bedrock.Prompts[2]);
        Assert.DoesNotContain(TenderMatchPrompt.Instructions, bedrock.Prompts[2]);
        Assert.Contains("CUSTOM SUMMARY GUIDANCE", bedrock.Prompts[3]);
        Assert.Contains(TenderSummaryPrompt.RequiredInstructions, bedrock.Prompts[3]);
        Assert.Contains("Bread supply", bedrock.Prompts[3]);
        Assert.DoesNotContain(TenderSummaryPrompt.Instructions, bedrock.Prompts[3]);
    }

    [Theory]
    [InlineData(120, 0, true)]
    [InlineData(121, 0, false)]
    [InlineData(0, 1200, true)]
    [InlineData(0, 1201, false)]
    public async Task BedrockMatchingAcceptsAndRejectsExplanationLengthBoundaries(int words, int characters, bool accepted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
        await db.Database.EnsureCreatedAsync();
        var reason = words > 0 ? string.Join(" ", Enumerable.Repeat("word", words)) : new string('x', characters);
        using var bedrock = new CapturingBedrockClient
        {
            MatchingText = JsonSerializer.Serialize(new { matches = new[] { new { tenderId = "42", reason, matchScore = 88 } } })
        };
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, Options.Create(new BedrockOptions { ModelId = "test" }),
            services.GetRequiredService<PromptSettings>(), NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var customer = new CustomerMatchEvidence("C-42", "10710", "Bakery", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Bread payment")]);
        var tenders = new[] { new TenderMatchEvidence("42", "Bread supply", null, null, []) };

        if (accepted)
            Assert.Equal(reason, Assert.Single(await evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None)).Reason);
        else
            await Assert.ThrowsAsync<TenderMatchEvaluationException>(() => evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":1}]}""", true)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":100}]}""", true)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":0}]}""", false)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":101}]}""", false)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match."}]}""", false)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":1.5}]}""", false)]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":"85"}]}""", false)]
    public async Task BedrockMatchingValidatesFitScore(string response, bool accepted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenderAiDbContext>().Database.EnsureCreatedAsync();
        using var bedrock = new CapturingBedrockClient { MatchingText = response };
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, Options.Create(new BedrockOptions { ModelId = "test" }),
            services.GetRequiredService<PromptSettings>(), NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var customer = new CustomerMatchEvidence("C-42", "10710", "Bakery", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Bread payment")]);
        var tenders = new[] { new TenderMatchEvidence("42", "Bread supply", null, null, []) };
        if (accepted)
        {
            Assert.InRange(Assert.Single(await evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None)).MatchScore, 1, 100);
            Assert.Single(bedrock.Prompts);
        }
        else
        {
            await Assert.ThrowsAsync<TenderMatchEvaluationException>(() => evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None));
            Assert.Equal(3, bedrock.Prompts.Count);
        }
    }

    [Theory]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match."}]}""")]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":null}]}""")]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":0}]}""")]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":101}]}""")]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":"85"}]}""")]
    [InlineData("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":85.5}]}""")]
    public async Task BedrockMatchingRetriesInvalidScoresWithExplicitCorrection(string invalidResponse)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenderAiDbContext>().Database.EnsureCreatedAsync();
        using var bedrock = new CapturingBedrockClient();
        bedrock.MatchingResponses.Enqueue(invalidResponse);
        bedrock.MatchingResponses.Enqueue("""{"matches":[{"tenderId":"42","reason":"Bread sales match.","matchScore":88}]}""");
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, Options.Create(new BedrockOptions { ModelId = "test" }),
            services.GetRequiredService<PromptSettings>(), NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var customer = new CustomerMatchEvidence("C-42", "10710", "Bakery", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Bread payment")]);
        var tenders = new[] { new TenderMatchEvidence("42", "Bread supply", null, null, []) };

        var match = Assert.Single(await evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None));
        Assert.Equal(88, match.MatchScore);
        Assert.Equal(2, bedrock.Prompts.Count);
        Assert.Contains("previous response had a missing, non-integer, or out-of-range matchScore", bedrock.Prompts[1]);
        Assert.Contains("Bread payment", bedrock.Prompts[1]);
        Assert.Contains(TenderMatchPrompt.RequiredInstructions, bedrock.Prompts[1]);
    }

    [Fact]
    public async Task InvalidScoresRemainErrorsAfterBoundedRetries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateServices(connection);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenderAiDbContext>().Database.EnsureCreatedAsync();
        using var bedrock = new CapturingBedrockClient
        {
            MatchingText = """{"matches":[{"tenderId":"42","reason":"Bread sales match."}]}"""
        };
        var evaluator = new BedrockTenderMatchEvaluator(bedrock, Options.Create(new BedrockOptions { ModelId = "test" }),
            services.GetRequiredService<PromptSettings>(), NullLogger<BedrockTenderMatchEvaluator>.Instance);
        var customer = new CustomerMatchEvidence("C-42", "10710", "Bakery", null, null,
            [new IncomingPaymentEvidence(new DateOnly(2026, 1, 1), "Bread payment")]);
        var tenders = new[] { new TenderMatchEvidence("42", "Bread supply", null, null, []) };

        var error = await Assert.ThrowsAsync<TenderMatchEvaluationException>(
            () => evaluator.EvaluateCustomerAsync(customer, tenders, CancellationToken.None));
        Assert.Contains("after 3 attempts", error.Message);
        Assert.Equal(3, bedrock.Prompts.Count);
        Assert.NotEqual(bedrock.Prompts[1], bedrock.Prompts[2]);
    }

    private static ServiceProvider CreateServices(SqliteConnection connection) => new ServiceCollection()
        .AddDbContext<TenderAiDbContext>(options => options.UseSqlite(connection))
        .AddSingleton<PromptSettings>()
        .BuildServiceProvider();

    private static TenderEntity CreateTender() => new()
    {
        Id = 42,
        DisplayNo = "42",
        Source = "UZEX",
        SourceUrl = "https://example.test/42",
        Name = "Bread supply",
        BuyerName = "School",
        CurrencyCode = "UZS"
    };

    private sealed class CapturingBedrockClient() : AmazonBedrockRuntimeClient(
        new BasicAWSCredentials("test", "test"),
        new AmazonBedrockRuntimeConfig { RegionEndpoint = RegionEndpoint.USEast1 })
    {
        public List<string> Prompts { get; } = [];
        public Queue<string> MatchingResponses { get; } = new();
        public string MatchingText { get; init; } = """{"matches":[]}""";

        public override async Task<InvokeModelResponse> InvokeModelAsync(InvokeModelRequest request, CancellationToken ct = default)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            using var json = JsonDocument.Parse(await reader.ReadToEndAsync(ct));
            var prompt = json.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
            Prompts.Add(prompt);
            var text = prompt.Contains("Customer and open tender evidence")
                ? (MatchingResponses.Count > 0 ? MatchingResponses.Dequeue() : MatchingText)
                : """{"title":"Fresh bread","summary":"Fresh bread for schools."}""";
            var response = JsonSerializer.Serialize(new { output = new { message = new { content = new[] { new { text } } } } });
            return new InvokeModelResponse { Body = new MemoryStream(Encoding.UTF8.GetBytes(response)) };
        }
    }
}
