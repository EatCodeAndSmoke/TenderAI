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
using System.Text.Json;
using System.Text.Json.Serialization;
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
        tender.BuyerRequirements.Add("Calibration certificate required.");
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
        Assert.Contains("Calibration certificate required.", prompt);
        Assert.DoesNotContain("Uzbek requirement", prompt);
        Assert.Contains("12", prompt);
        Assert.Contains("2400", prompt);
    }

    [Fact]
    public void UZEXMappingPreservesDetailedTitlesBothDescriptionsAndTechnicalPropertiesForThePrompt()
    {
        const string detailedDescription = "Фаол турдаги водород частота эталонини харид қилиш. "
            + "Техник параметрлар батафсил техник топшириқда ва харид хужжатларида келтирилган. "
            + "/ Водородный эталон частоты активного типа. Подробное описание и технические требования приведены "
            + "в техническом задании и закупочной документации";
        const string additionalDescription = "Радиочастотный диапазон: 1–100 MHz";
        var product = JsonSerializer.SerializeToElement(new
        {
            OrderNum = 1,
            Product_Name = "Водородный эталон частоты активного типа",
            Category_Name = "Средства измерения",
            Quantity = 1,
            Cost = 179741056.25m,
            Js_Properties = new[]
            {
                new { Property_Name = "Единица измерения", User_Value = "комплект" },
                new { Property_Name = "Диапазон частоты", User_Value = "1–100 MHz" }
            }
        });
        var source = new UzexTrade
        {
            Name = "Водородный эталон частоты активного типа",
            Description = detailedDescription,
            AddonDescription = additionalDescription,
            BudgetProducts = JsonSerializer.SerializeToElement(new[] { product })
        };
        var tender = UzexMapping.ToTenderEntity(
            new UzexTradeListItem { Id = 16, Name = "Technical equipment" },
            source,
            "https://uzex.test/lot/16",
            DateTime.UtcNow);

        var prompt = TenderSummaryPrompt.Build(tender);

        Assert.Equal("Technical equipment", tender.Name);
        Assert.Equal("Водородный эталон частоты активного типа", tender.DetailedName);
        Assert.Equal(detailedDescription, tender.Description);
        Assert.Equal(additionalDescription, tender.AdditionalDescription);
        var productLine = Assert.Single(tender.Products);
        Assert.Equal("комплект", productLine.Unit);
        Assert.Contains("Диапазон частоты: 1–100 MHz", productLine.TechnicalSpecifications);
        Assert.Contains("Водородный эталон частоты активного типа", prompt);
        Assert.Contains(detailedDescription, prompt);
        Assert.Contains(additionalDescription, prompt);
        Assert.Contains("Диапазон частоты: 1–100 MHz", prompt);
        Assert.Contains("комплект", prompt);
        Assert.Contains("179741056.25", prompt);
        Assert.Contains("Start by naming the specific product", prompt);
        Assert.Contains("Never call it only \"equipment\"", prompt);
    }

    [Fact]
    public void MapsEncodedHydrogenStandardPayloadWithSelectedBuyerRequirementsAndRawIntegerCost()
    {
        const string productName = "Водородный эталон частоты активного типа";
        const string description = "Фаол турдаги водород частота эталонини харид қилиш. "
            + "Техник параметрлар батафсил техник топшириқда ва харид хужжатларида келтирилган. "
            + "/ Водородный эталон частоты активного типа. Подробное описание и технические требования приведены "
            + "в техническом задании и закупочной документации";
        var buyerFields = new[]
        {
            new { field_name = "Calibration certificate", value = "CIPM-MRA/ILAC MRA calibration certificate required." },
            new { field_name = "Staff training", value = "Train 3 staff." },
            new { field_name = "Year of manufacture", value = "Manufacture year >=2026." },
            new { field_name = "Installation", value = "Installation, commissioning, and trial operation required." },
            new { field_name = "Packaging and transport", value = "Packaging and transport included." },
            new { field_name = "Delivery", value = "Delivery <=250 days DDP Tashkent." },
            new { field_name = "Warranty", value = "Warranty >=12 months." },
            new { field_name = "Bidder experience", value = "At least 5 years of experience." },
            new { field_name = "Scoring", value = "40 points for past performance." },
            new { field_name = "Tax debt", value = "No outstanding tax debt." }
        };
        var trade = DeserializeTrade(new
        {
            id = 511674,
            name = productName,
            description,
            budget_products = JsonSerializer.Serialize(new[]
            {
                new
                {
                    OrderNum = 1,
                    Product_Name = productName,
                    Quantity = 1,
                    Js_Properties = """[{"Property_Name":"Единица измерения","User_Value":"компл."}]"""
                }
            }),
            js_fields = JsonSerializer.Serialize(buyerFields),
            js_qualification_fields = """[{"OrderNum":1,"Name":"Bidder qualification paperwork"}]""",
            tech_file_path = "/documents/technical-assignment.pdf",
            tech_doc_file_path = "/documents/procurement.pdf"
        });
        var listItem = DeserializeTradeListItem(new
        {
            id = 511674,
            name = "Technical equipment",
            cost = 17974105625,
            currency_codeabc = "UZS"
        });
        var tender = UzexMapping.ToTenderEntity(
            listItem,
            trade,
            "https://uzex.test/lot/511674",
            DateTime.UtcNow);

        var prompt = TenderSummaryPrompt.Build(tender);

        Assert.Equal(17974105625m, tender.StartPrice);
        Assert.Equal("компл.", Assert.Single(tender.Products).Unit);
        Assert.Equal(7, tender.BuyerRequirements.Count);
        Assert.Contains(productName, prompt);
        Assert.Contains("CIPM-MRA/ILAC MRA calibration certificate", prompt);
        Assert.Contains("Train 3 staff", prompt);
        Assert.Contains("Manufacture year >=2026", prompt);
        Assert.Contains("commissioning, and trial operation", prompt);
        Assert.Contains("Packaging and transport", prompt);
        Assert.Contains("Delivery <=250 days DDP Tashkent", prompt);
        Assert.Contains("Warranty >=12 months", prompt);
        Assert.DoesNotContain("Bidder experience", prompt);
        Assert.DoesNotContain("Bidder qualification paperwork", prompt);
        Assert.DoesNotContain("40 points", prompt);
        Assert.DoesNotContain("No outstanding tax debt", prompt);
        Assert.Contains("TechnicalDocumentFilesReferencedButNotParsed\":true", prompt);
        Assert.Contains("their contents have not been read", prompt);
        Assert.Contains("17974105625", prompt);
        Assert.DoesNotContain("179,741,056,25", prompt);
    }

    [Fact]
    public void MapsEncodedBudgetProductArrayAndEveryProductSpecification()
    {
        var trade = DeserializeTrade(new
        {
            id = 518436,
            name = "Medical products",
            budget_products = JsonSerializer.Serialize(new[]
            {
                new
                {
                    OrderNum = 1,
                    Product_Name = "Product line 1",
                    Description = "Раствор для инфузий",
                    Js_Properties = JsonSerializer.Serialize(new[]
                    {
                        new { Property_Name = "Дозировка и объем", User_Value = "100 mg/ml 50 ml" },
                        new { Property_Name = "Форма выпуска", User_Value = "раствор для инфузий" },
                        new { Property_Name = "Единица измерения", User_Value = "флакон" },
                        new { Property_Name = "Упаковка", User_Value = "N1" }
                    })
                },
                new
                {
                    OrderNum = 2,
                    Product_Name = "Product line 2",
                    Description = "",
                    Js_Properties = """[{"Property_Name":"Package","User_Value":"N2"}]"""
                }
            }),
            js_fields = "[]",
            tech_doc_file_path = "/documents/technical-requirements.pdf"
        });
        var tender = UzexMapping.ToTenderEntity(
            new UzexTradeListItem { Id = 518436, Name = "Medical products" },
            trade,
            "https://uzex.test/lot/518436",
            DateTime.UtcNow);

        var prompt = TenderSummaryPrompt.Build(tender);

        Assert.Equal(2, tender.Products.Count);
        Assert.Equal("Product line 1", tender.Products[0].ProductName);
        Assert.Equal("Product line 2", tender.Products[1].ProductName);
        Assert.Contains("100 mg/ml 50 ml", tender.Products[0].TechnicalSpecifications);
        Assert.Contains("раствор для инфузий", tender.Products[0].TechnicalSpecifications);
        Assert.Contains("флакон", tender.Products[0].TechnicalSpecifications);
        Assert.Contains("N1", tender.Products[0].TechnicalSpecifications);
        Assert.Contains("N2", tender.Products[1].TechnicalSpecifications);
        Assert.Contains("Product line 1", prompt);
        Assert.Contains("Product line 2", prompt);
        Assert.Contains("100 mg/ml 50 ml", prompt);
        Assert.Contains("N1", prompt);
        Assert.Contains("TechnicalDocumentFilesReferencedButNotParsed\":true", prompt);
    }

    [Fact]
    public async Task PersistsBuyerRequirementsAndTechnicalDocumentFlag()
    {
        await using var db = await CreateDatabaseAsync();
        var tender = CreateTender();
        tender.BuyerRequirements.Add("Delivery within <=250 days DDP Tashkent.");
        tender.HasUnparsedTechnicalDocuments = true;
        tender.Products.Add(new TenderProductEntity
        {
            OrderNum = 1,
            ProductName = "Product",
            TechnicalSpecifications = "Dosage: 100 mg/ml 50 ml"
        });
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var storedTender = await db.Tenders.Include(item => item.Products).SingleAsync();

        Assert.Equal("Delivery within <=250 days DDP Tashkent.", Assert.Single(storedTender.BuyerRequirements));
        Assert.True(storedTender.HasUnparsedTechnicalDocuments);
        Assert.Equal("Dosage: 100 mg/ml 50 ml", Assert.Single(storedTender.Products).TechnicalSpecifications);
    }

    [Fact]
    public void PromptRequiresPlainDisclosureWhenLinkedTechnicalRequirementsAreNotAvailable()
    {
        var tender = CreateTender();
        tender.Description = "Specifications are in the technical assignment and procurement documents.";

        var prompt = TenderSummaryPrompt.Build(tender);

        Assert.Contains("were not included in the available data", prompt);
        Assert.Contains("Do not imply you have read those documents", prompt);
        Assert.Contains(tender.Description, prompt);
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
        completed.SummaryVersion = TenderSummaryService.CurrentSummaryVersion;
        var failed = CreateTender(3);
        failed.SummaryError = "Previous Bedrock failure";
        var stale = CreateTender(4);
        stale.Summary = "The buyer wants technical equipment.";
        stale.SummaryVersion = 1;
        db.Tenders.AddRange(missing, completed, failed, stale);
        await db.SaveChangesAsync();

        var summarizedIds = new ConcurrentBag<long>();
        var service = new TenderSummaryService(
            new DelegateSummarizer((tender, _) =>
            {
                summarizedIds.Add(tender.Id);
                return Task.FromResult($"Summary {tender.Id}");
            }),
            NullLogger<TenderSummaryService>.Instance);

        Assert.Equal(3, await service.SummarizeMissingTendersAsync(db, CancellationToken.None));
        Assert.Equal(new long[] { 1, 3, 4 }, summarizedIds.Order());
        Assert.Equal("Summary 1", missing.Summary);
        Assert.Equal("Already stored", completed.Summary);
        Assert.Equal("Summary 3", failed.Summary);
        Assert.Null(failed.SummaryError);
        Assert.Equal("Summary 4", stale.Summary);
        Assert.Equal(TenderSummaryService.CurrentSummaryVersion, stale.SummaryVersion);
        Assert.Equal(0, await service.SummarizeMissingTendersAsync(db, CancellationToken.None));
        Assert.Equal(3, summarizedIds.Count);
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

    [Fact]
    public async Task ImportStreamsAndPersistsEachTenderBeforeFetchingTheNext()
    {
        await using var db = await CreateDatabaseAsync();
        var handler = new TenderApiHandler(3);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var summaryService = new TenderSummaryService(
            new DelegateSummarizer((tender, _) => Task.FromResult($"Summary {tender.Id}")),
            NullLogger<TenderSummaryService>.Instance);
        var importer = new TenderImportService(db, new UzexClient(http), summaryService,
            Options.Create(new UzexOptions { ApiBaseUrl = "https://uzex.test/", LotUrl = "https://uzex.test/lot/{0}" }),
            NullLogger<TenderImportService>.Instance);

        await using var stream = importer.LoadNewStreamingAsync(3, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(1, handler.DetailsRequested);
        Assert.Equal(1, await db.Tenders.CountAsync());
        Assert.Equal("Summary 1", stream.Current.Summary);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(2, handler.DetailsRequested);
        Assert.Equal(2, await db.Tenders.CountAsync());
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

    private static UzexTrade DeserializeTrade<T>(T payload)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<UzexTrade>(json, options)
            ?? throw new InvalidOperationException("Test UZEX fixture did not deserialize.");
    }

    private static UzexTradeListItem DeserializeTradeListItem<T>(T payload)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<UzexTradeListItem>(json, options)
            ?? throw new InvalidOperationException("Test UZEX list fixture did not deserialize.");
    }

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
                CREATE TABLE "TenderProducts" ("Id" INTEGER PRIMARY KEY);
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
            SELECT "Id", "Name", "TranslationError", "Summary", "SummaryVersion", "SummaryError",
                "DetailedName", "AdditionalDescription", "HasUnparsedTechnicalDocuments", "BuyerRequirements", "EnglishTitle"
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
        Assert.True(reader.IsDBNull(5));
        Assert.True(reader.IsDBNull(6));
        Assert.True(reader.IsDBNull(7));
        Assert.True(reader.IsDBNull(8));
        Assert.Equal("[]", reader.GetString(9));
        Assert.True(reader.IsDBNull(10));

        using var products = connection.CreateCommand();
        products.CommandText = "SELECT \"TechnicalSpecifications\" FROM \"TenderProducts\" LIMIT 0;";
        using var productsReader = products.ExecuteReader();
        Assert.Equal(1, productsReader.FieldCount);
    }
}
