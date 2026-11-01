using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using Xunit;

namespace TenderAI.Api.Tests;

public class TenderTranslationServiceTests
{
    [Fact]
    public async Task TranslatesTenderAndProductsWithoutChangingOriginalText()
    {
        var translator = new DelegateTranslator((text, _) => Task.FromResult($"English: {text}"));
        var service = new TenderTranslationService(translator, NullLogger<TenderTranslationService>.Instance);
        var tender = CreateTender();
        tender.Requirements.Add("Original requirement");
        tender.Products.Add(new TenderProductEntity
        {
            OrderNum = 1,
            ProductName = "Original product",
            CategoryName = "Original category",
            Description = "Original product description",
            Unit = "Original unit",
            WarrantyTermUnit = "Original warranty unit"
        });

        await service.TranslateTendersAsync([tender], CancellationToken.None);

        var translation = Assert.Single(tender.Translations);
        Assert.Equal("English: Original tender", translation.Name);
        Assert.Equal("English: Original buyer", translation.BuyerName);
        Assert.Equal(new[] { "English: Original requirement" }, translation.Requirements);
        Assert.Null(tender.TranslationError);
        Assert.Equal("Original tender", tender.Name);
        Assert.Equal("Original buyer", tender.BuyerName);
        Assert.Equal("Original requirement", Assert.Single(tender.Requirements));

        var productTranslation = Assert.Single(Assert.Single(tender.Products).Translations);
        Assert.Equal("English: Original product", productTranslation.ProductName);
        Assert.Equal("English: Original category", productTranslation.CategoryName);
        Assert.Equal("English: Original product description", productTranslation.Description);
        Assert.Equal("English: Original unit", productTranslation.Unit);
        Assert.Equal("English: Original warranty unit", productTranslation.WarrantyTermUnit);
        Assert.Equal("Original product", tender.Products[0].ProductName);
    }

    [Fact]
    public async Task RecordsSpecificTranslationFailureAndKeepsOriginalFallback()
    {
        var translator = new DelegateTranslator((text, _) =>
            text == "Unavailable buyer"
                ? Task.FromException<string>(new InvalidOperationException("service unavailable"))
                : Task.FromResult($"English: {text}"));
        var service = new TenderTranslationService(translator, NullLogger<TenderTranslationService>.Instance);
        var tender = CreateTender();
        tender.BuyerName = "Unavailable buyer";
        tender.Requirements.Add("Original requirement");

        await service.TranslateTendersAsync([tender], CancellationToken.None);

        var translation = Assert.Single(tender.Translations);
        Assert.Null(translation.BuyerName);
        Assert.Equal("English: Original tender", translation.Name);
        Assert.Equal(new[] { "English: Original requirement" }, translation.Requirements);
        Assert.Contains("BuyerName: service unavailable", tender.TranslationError);
        Assert.Equal("Unavailable buyer", tender.BuyerName);
    }

    [Fact]
    public async Task LimitsParallelTenderTranslations()
    {
        var active = 0;
        var maximum = 0;
        var translator = new DelegateTranslator(async (text, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximum, current);
            try
            {
                await Task.Delay(25, ct);
                return text;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var service = new TenderTranslationService(translator, NullLogger<TenderTranslationService>.Instance);
        var tenders = Enumerable.Range(1, 8)
            .Select(id => CreateTender(id, buyerName: ""))
            .ToList();

        await service.TranslateTendersAsync(tenders, CancellationToken.None);

        Assert.InRange(maximum, 2, 4);
    }

    [Fact]
    public async Task BackfillsOnlyExistingTendersWithoutEnglishTranslations()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var missingTranslation = CreateTender(1);
        var alreadyTranslated = CreateTender(2);
        alreadyTranslated.Translations.Add(new TenderTranslationEntity
        {
            Tender = alreadyTranslated,
            Lang = "en",
            Name = "Stored English",
            TranslatedAt = DateTime.UtcNow
        });
        db.Tenders.AddRange(missingTranslation, alreadyTranslated);
        await db.SaveChangesAsync();

        var translatedInputs = new List<string>();
        var translator = new DelegateTranslator((text, _) =>
        {
            translatedInputs.Add(text);
            return Task.FromResult($"English: {text}");
        });
        var service = new TenderTranslationService(translator, NullLogger<TenderTranslationService>.Instance);

        var count = await service.TranslateMissingTendersAsync(db, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(new[] { "Original tender", "Original buyer" }, translatedInputs);
        Assert.Equal("English: Original tender", missingTranslation.Translations.Single().Name);
        Assert.Equal("Stored English", alreadyTranslated.Translations.Single().Name);
        Assert.Equal(2, await db.TenderTranslations.CountAsync());
    }

    [Fact]
    public void SplitsLongTextAtUtf8LimitWithoutDroppingTextOrBreakingSurrogatePairs()
    {
        var text = new string('a', 9_996) + "🙂" + "bc";

        var chunks = TranslateTextChunker.Split(text);

        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk =>
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(chunk) <= TranslateTextChunker.MaxUtf8Bytes));
        Assert.Equal(10_000, System.Text.Encoding.UTF8.GetByteCount(chunks[0]));
        Assert.Equal("bc", chunks[1]);
    }

    private static TenderEntity CreateTender(long id = 1, string buyerName = "Original buyer") => new()
    {
        Id = id,
        DisplayNo = $"LOT-{id}",
        Source = "UZEX",
        SourceUrl = $"https://example.test/{id}",
        Name = "Original tender",
        BuyerName = buyerName,
        CurrencyCode = "UZS"
    };

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

    private sealed class DelegateTranslator(
        Func<string, CancellationToken, Task<string>> translate) : ITextTranslator
    {
        public Task<string> TranslateAsync(string text, CancellationToken ct) => translate(text, ct);
    }
}

public class DatabaseSchemaTests
{
    [Fact]
    public void AddsTranslationErrorColumnWithoutRemovingExistingTenderData()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "Tenders" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                INSERT INTO "Tenders" ("Id", "Name") VALUES (42, 'Existing tender');
                """;
            command.ExecuteNonQuery();
        }

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TenderAiDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new TenderAiDbContext(options);

        DatabaseSchema.AddMissingColumns(db);
        DatabaseSchema.AddMissingColumns(db);

        using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT "Id", "Name", "TranslationError"
            FROM "Tenders"
            WHERE "Id" = 42;
            """;
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(42L, reader.GetInt64(0));
        Assert.Equal("Existing tender", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
    }
}
