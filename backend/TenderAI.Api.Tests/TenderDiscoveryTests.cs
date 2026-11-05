using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    public async Task DiscoveryEndpointDefaultsToThreeMatchesOrFewerWhenUnavailable(int available, int expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.AddRange(Enumerable.Range(1, available).Select(Tender));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        var controller = new MatchesController(db, Matcher(db), NullLogger<MatchesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        await controller.Discover("C-1", null, Importer(db, http), CancellationToken.None);
        body.Position = 0;
        var payload = await new StreamReader(body, Encoding.UTF8).ReadToEndAsync();
        var events = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line["data: ".Length..]))
            .ToList();
        try
        {
            Assert.Equal(expected, events.Count(eventJson => eventJson.RootElement.GetProperty("type").GetString() == "match"));
            var completed = Assert.Single(events, eventJson => eventJson.RootElement.GetProperty("type").GetString() == "completed");
            Assert.Equal(expected, completed.RootElement.GetProperty("count").GetInt32());
        }
        finally
        {
            events.ForEach(eventJson => eventJson.Dispose());
        }
    }

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
    public async Task DefaultDiscoveryImportsUntilThreeMatchesAreFound()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.Add(Tender(1));
        await db.SaveChangesAsync();
        var handler = new UzexHandler(4);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        var controller = new MatchesController(db, Matcher(db), NullLogger<MatchesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        await controller.Discover("C-1", null, Importer(db, http), CancellationToken.None);
        body.Position = 0;
        var payload = await new StreamReader(body, Encoding.UTF8).ReadToEndAsync();
        Assert.Equal(3, payload.Split("\"type\":\"match\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("\"phase\":\"loading\"", payload);
        Assert.Contains("\"phase\":\"matching\"", payload);
        Assert.Contains("\"checked\":", payload);
        Assert.True(handler.ListCalls > 0);
    }

    [Fact]
    public async Task ApplicationPersistsIndependentlyOfFeedbackAndIsReturnedOnLaterSearches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(Customer("C-1"), Customer("C-2"));
        db.Tenders.AddRange(Tender(1), Tender(2));
        await db.SaveChangesAsync();
        var controller = new CustomersController(db);
        Assert.IsType<ConflictObjectResult>((await controller.Apply("C-1", 1, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Apply("C-404", 1, CancellationToken.None)).Result);

        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) { }
        Assert.IsType<NoContentResult>(await controller.SetFeedback("C-1", 1, new TenderFeedbackRequest("like"), CancellationToken.None));
        var application = (await controller.Apply("c-1", 1, CancellationToken.None)).Value!;
        Assert.Equal("applied", application.Status);
        Assert.Equal(DateTimeKind.Utc, application.AppliedAt.Kind);
        Assert.Equal(application, (await controller.Apply("C-1", 1, CancellationToken.None)).Value);
        Assert.IsType<ConflictObjectResult>((await controller.Apply("C-2", 1, CancellationToken.None)).Result);

        db.ChangeTracker.Clear();
        var loaded = Assert.Single((await controller.GetMatches("C-1", CancellationToken.None)).Value!);
        Assert.Equal("like", loaded.Feedback);
        Assert.Equal(application.AppliedAt, loaded.AppliedAt);
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(2, Assert.Single(events, e => e.Type == "match").Tender!.Id);
        var savedFirst = (await controller.GetMatches("C-1", CancellationToken.None)).Value!.Single(match => match.LotId == "1");
        Assert.Equal("like", savedFirst.Feedback);
        Assert.Equal(application.AppliedAt, savedFirst.AppliedAt);
        Assert.Equal(1, await db.TenderApplications.CountAsync());

        Assert.IsType<NoContentResult>(await controller.SetFeedback("C-1", 1, new TenderFeedbackRequest("dislike"), CancellationToken.None));
        Assert.Equal("2", Assert.Single((await controller.GetMatches("C-1", CancellationToken.None)).Value!).LotId);
        Assert.Single(await db.TenderApplications.AsNoTracking().ToListAsync());
        var later = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) later.Add(item);
        Assert.DoesNotContain(later, e => e.Type == "match");
        Assert.Equal("dislike", (await db.TenderFeedback.AsNoTracking().SingleAsync()).Value);
        Assert.IsType<NoContentResult>(await controller.Delete("C-1", CancellationToken.None));
        Assert.Empty(await db.TenderApplications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TenderFeedback.AsNoTracking().ToListAsync());
        Assert.True(await db.Customers.AnyAsync(c => c.CustomerId == "C-2"));
    }

    [Fact]
    public async Task ApplicationRequiresAnOpenMatchingTenderAndRejectsDislikedTenders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.AddRange(Tender(1), Tender(2));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 2, Importer(db, http), CancellationToken.None)) { }
        var controller = new CustomersController(db);
        await controller.SetFeedback("C-1", 1, new TenderFeedbackRequest("dislike"), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>((await controller.Apply("C-1", 1, CancellationToken.None)).Result);
        var expired = await db.Tenders.SingleAsync(t => t.Id == 2);
        expired.EndDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>((await controller.Apply("C-1", 2, CancellationToken.None)).Result);
        Assert.Empty(await db.TenderApplications.ToListAsync());
    }

    [Fact]
    public async Task ExistingDatabaseGetsApplicationTableWithoutLosingFeedback()
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
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"TenderApplications\"");

        DatabaseSchema.AddMissingTables(db);
        DatabaseSchema.AddMissingTables(db);
        db.ChangeTracker.Clear();
        Assert.Equal("like", (await db.TenderFeedback.SingleAsync()).Value);
        Assert.Empty(await db.TenderApplications.ToListAsync());
        db.TenderApplications.Add(new TenderApplicationEntity { CustomerId = "C-1", TenderId = 1, AppliedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Single(await db.TenderApplications.ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"TenderDiscoverySeen\"");
        DatabaseSchema.AddMissingTables(db);
        DatabaseSchema.AddMissingTables(db);
        Assert.Empty(await db.TenderDiscoverySeen.AsNoTracking().ToListAsync());
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
    public async Task DeleteRemovesCustomerMatchesAndAllowsReimportWithoutAffectingOtherTenders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(Customer("C-1"), Customer("C-2"));
        var first = Tender(1);
        var other = Tender(2);
        first.Products.Add(new TenderProductEntity { ProductName = "Bread", OrderNum = 1 });
        other.Products.Add(new TenderProductEntity { ProductName = "Bread", OrderNum = 1 });
        db.Tenders.AddRange(first, other);
        await db.SaveChangesAsync();
        foreach (var customerId in new[] { "C-1", "C-2" })
        {
            await foreach (var _ in Matcher(db).RunStreamingAsync(CancellationToken.None, customerId)) { }
            foreach (var tenderId in new long[] { 1, 2 })
            {
                db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = customerId, TenderId = tenderId, Value = "like" });
                db.TenderApplications.Add(new TenderApplicationEntity { CustomerId = customerId, TenderId = tenderId, AppliedAt = DateTime.UtcNow });
                db.TenderDiscoverySeen.Add(new TenderDiscoverySeenEntity { CustomerId = customerId, TenderId = tenderId });
            }
        }
        await db.SaveChangesAsync();
        Assert.Equal(4, await db.Notifications.CountAsync());
        var controller = new TendersController(db);
        Assert.IsType<NoContentResult>(await controller.Delete(1, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(2, Assert.Single((await controller.GetAll(CancellationToken.None, true)).Value!).Id);
        Assert.Equal(2, Assert.Single(await db.Tenders.ToListAsync()).Id);
        Assert.Equal(2, Assert.Single(await db.TenderProducts.ToListAsync()).TenderId);
        Assert.All(await db.TenderFeedback.ToListAsync(), feedback => Assert.Equal(2, feedback.TenderId));
        Assert.Equal(2, await db.TenderFeedback.CountAsync());
        Assert.All(await db.TenderApplications.ToListAsync(), application => Assert.Equal(2, application.TenderId));
        Assert.Equal(2, await db.TenderApplications.CountAsync());
        Assert.All(await db.Notifications.ToListAsync(), notification => Assert.Equal("2", notification.LotId));
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.All(await db.TenderDiscoverySeen.ToListAsync(), seen => Assert.Equal(2, seen.TenderId));
        Assert.Equal(2, await db.TenderDiscoverySeen.CountAsync());
        Assert.Equal(2, await db.Customers.CountAsync());
        var handler = new UzexHandler(2);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://uzex.test/") };
        var imported = await Importer(db, http).LoadNewAsync(1, CancellationToken.None);
        Assert.Equal(1, Assert.Single(imported).Id);
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(1, Assert.Single(events, item => item.Type == "match").Tender!.Id);
    }

    [Fact]
    public async Task PreviouslySoftDeletedTenderCanBeReimportedAndItsOldCustomerStateIsRemoved()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var deleted = Tender(1);
        deleted.Products.Add(new TenderProductEntity { ProductName = "Old product", OrderNum = 1 });
        db.Tenders.Add(deleted);
        await db.SaveChangesAsync();
        using var emptyHttp = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, emptyHttp), CancellationToken.None)) { }
        db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = "C-1", TenderId = 1, Value = "like" });
        db.TenderApplications.Add(new TenderApplicationEntity { CustomerId = "C-1", TenderId = 1, AppliedAt = DateTime.UtcNow });
        deleted.IsDeleted = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using var http = new HttpClient(new UzexHandler(1)) { BaseAddress = new Uri("https://uzex.test/") };
        var reimported = Assert.Single(await Importer(db, http).LoadNewAsync(1, CancellationToken.None));
        Assert.Equal(1, reimported.Id);
        db.ChangeTracker.Clear();
        Assert.False((await db.Tenders.SingleAsync()).IsDeleted);
        Assert.Empty(await db.TenderProducts.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.TenderFeedback.ToListAsync());
        Assert.Empty(await db.TenderApplications.ToListAsync());
        Assert.Empty(await db.TenderDiscoverySeen.ToListAsync());
    }

    [Fact]
    public async Task FailedReimportKeepsPreviouslySoftDeletedTenderAndItsData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var deleted = Tender(1);
        deleted.IsDeleted = true;
        deleted.Products.Add(new TenderProductEntity { ProductName = "Old product", OrderNum = 1 });
        db.Tenders.Add(deleted);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(1, failDetails: true)) { BaseAddress = new Uri("https://uzex.test/") };
        Assert.Empty(await Importer(db, http).LoadNewAsync(1, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.True((await db.Tenders.SingleAsync()).IsDeleted);
        Assert.Equal("Old product", (await db.TenderProducts.SingleAsync()).ProductName);
    }

    [Fact]
    public async Task ClearPermanentlyDeletesTendersAndRelatedRecordsAndAllowsReimport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var active = Tender(1);
        active.Products.Add(new TenderProductEntity { ProductName = "Bread", OrderNum = 1 });
        var deleted = Tender(2);
        deleted.IsDeleted = true;
        db.Tenders.AddRange(active, deleted);
        db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = "C-1", TenderId = 1, Value = "like" });
        db.TenderApplications.Add(new TenderApplicationEntity { CustomerId = "C-1", TenderId = 1, AppliedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        using var emptyHttp = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, emptyHttp), CancellationToken.None)) { }
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.IsType<NoContentResult>(await new TendersController(db).Clear(CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.Tenders.ToListAsync());
        Assert.Empty(await db.TenderProducts.ToListAsync());
        Assert.Empty(await db.TenderFeedback.ToListAsync());
        Assert.Empty(await db.TenderApplications.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.TenderDiscoverySeen.AsNoTracking().ToListAsync());
        Assert.Single(await db.Customers.ToListAsync());
        using var http = new HttpClient(new UzexHandler(2)) { BaseAddress = new Uri("https://uzex.test/") };
        Assert.Equal(new long[] { 1, 2 }, (await Importer(db, http).LoadNewAsync(2, CancellationToken.None)).Select(t => t.Id));
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
        Assert.Equal(50, completed.Checked);
        Assert.Equal(0, completed.Count);
        Assert.Equal(50, await db.Tenders.CountAsync());
        db.ChangeTracker.Clear();
        var next = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) next.Add(item);
        Assert.Equal(10, next.Single(e => e.Type == "completed").Imported);
        Assert.Equal(10, next.Single(e => e.Type == "completed").Checked);
        Assert.Equal(60, await db.TenderDiscoverySeen.CountAsync());
    }

    [Fact]
    public async Task DiscoveryStreamsProgressAndSuppressesDuplicatesWithMatchingBuyerProductAndTerms()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var one = Tender(1);
        var duplicate = Tender(2);
        one.StartDate = new DateTime(2026, 10, 20, 11, 59, 28);
        duplicate.StartDate = one.StartDate.AddSeconds(31);
        one.EndDate = DateTime.UtcNow.AddHours(5).Date.AddDays(10).AddHours(11).AddMinutes(59).AddSeconds(28);
        duplicate.EndDate = one.EndDate.AddSeconds(31);
        duplicate.StartPrice = one.StartPrice;
        duplicate.BuyerName = " BUYER ";
        one.Products.Add(new TenderProductEntity { ProductName = "Bread", Quantity = 10, Unit = "kg", OrderNum = 1 });
        duplicate.Products.Add(new TenderProductEntity { ProductName = " bread ", Quantity = 10, Unit = "KG", OrderNum = 1 });
        var differentQuantity = Tender(3);
        differentQuantity.StartDate = one.StartDate;
        differentQuantity.EndDate = one.EndDate.AddSeconds(73);
        differentQuantity.StartPrice = one.StartPrice;
        differentQuantity.Products.Add(new TenderProductEntity { ProductName = "Bread", Quantity = 20, Unit = "kg", OrderNum = 1 });
        var differentPrice = Tender(4);
        differentPrice.StartDate = one.StartDate;
        differentPrice.EndDate = one.EndDate.AddSeconds(73);
        differentPrice.Products.Add(new TenderProductEntity { ProductName = "Bread", Quantity = 10, Unit = "kg", OrderNum = 1 });
        db.Tenders.AddRange(one, duplicate, differentQuantity, differentPrice);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 3, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(new long[] { 1, 3, 4 }, events.Where(e => e.Type == "match").Select(e => e.Tender!.Id));
        Assert.Contains(events, e => e.Type == "progress" && e.Phase == "matching" && e.Checked == 4 && e.Count == 0);
        Assert.Equal(4, events.Single(e => e.Type == "completed").Checked);
        Assert.Equal(4, await db.TenderDiscoverySeen.CountAsync());
        Assert.Equal(3, (await new CustomersController(db).GetDiscoveries("C-1", CancellationToken.None)).Value!.Count());
    }

    [Fact]
    public async Task ExistingDuplicateMatchesAreCollapsedAndDislikeHidesAllCopies()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var one = Tender(1);
        var duplicate = Tender(2);
        duplicate.StartPrice = one.StartPrice;
        one.StartDate = new DateTime(2026, 10, 20, 11, 59, 28);
        duplicate.StartDate = one.StartDate.AddSeconds(31);
        one.EndDate = DateTime.UtcNow.AddHours(5).Date.AddDays(10).AddHours(11).AddMinutes(59).AddSeconds(28);
        duplicate.EndDate = one.EndDate.AddSeconds(31);
        db.Tenders.AddRange(one, duplicate);
        await db.SaveChangesAsync();
        await foreach (var _ in Matcher(db).RunStreamingAsync(CancellationToken.None, "C-1")) { }
        Assert.Equal(2, await db.Notifications.CountAsync());
        var controller = new CustomersController(db);
        var shown = Assert.Single((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
        Assert.Single((await controller.GetMatches("C-1", CancellationToken.None)).Value!);
        await controller.SetFeedback("C-1", shown.Tender.Id, new TenderFeedbackRequest("dislike"), CancellationToken.None);
        Assert.Empty((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
        var third = Tender(3);
        third.StartPrice = one.StartPrice;
        third.StartDate = one.StartDate.AddSeconds(73);
        third.EndDate = one.EndDate.AddSeconds(73);
        db.Tenders.Add(third);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.DoesNotContain(events, e => e.Type == "match");
        Assert.Empty((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task DuplicateGroupPrefersOpenCopyAndHonorsDislikeOnExpiredCopy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        var now = DateTime.UtcNow.AddHours(5);
        var closed = Tender(1);
        var open = Tender(2);
        closed.EndDate = now.Date.AddDays(1).AddTicks(-1);
        open.EndDate = closed.EndDate;
        open.StartPrice = closed.StartPrice;
        db.Tenders.AddRange(closed, open);
        await db.SaveChangesAsync();
        await foreach (var _ in Matcher(db).RunStreamingAsync(CancellationToken.None, "C-1")) { }
        var controller = new CustomersController(db);
        Assert.NotNull((await controller.Apply("C-1", closed.Id, CancellationToken.None)).Value);

        closed.EndDate = now.Date;
        await db.SaveChangesAsync();
        var shown = Assert.Single((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
        Assert.Equal(open.Id, shown.Tender.Id);
        Assert.True(shown.IsOpen);
        Assert.Equal(open.Id.ToString(), Assert.Single((await controller.GetMatches("C-1", CancellationToken.None)).Value!).LotId);

        await controller.SetFeedback("C-1", closed.Id, new TenderFeedbackRequest("dislike"), CancellationToken.None);
        Assert.Empty((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
        Assert.Empty((await controller.GetMatches("C-1", CancellationToken.None)).Value!);
        Assert.Single(await db.TenderApplications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RepeatedSearchesResumePerCustomerAndKeepSavedMatchesUntilDisliked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(Customer("C-1"), Customer("C-2"));
        db.Tenders.AddRange(Enumerable.Range(1, 8).Select(Tender));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var evaluator = new EvenMatchEvaluator();
        var matcher = new MatchingService(db, evaluator, TimeProvider.System, NullLogger<MatchingService>.Instance);
        var first = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) first.Add(item);
        Assert.Equal(2, Assert.Single(first, e => e.Type == "match").Tender!.Id);
        Assert.Equal(new long[] { 1, 2 }, await db.TenderDiscoverySeen.AsNoTracking().Where(s => s.CustomerId == "C-1").OrderBy(s => s.TenderId).Select(s => s.TenderId).ToArrayAsync());

        db.ChangeTracker.Clear();
        var second = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 2, Importer(db, http), CancellationToken.None)) second.Add(item);
        Assert.Equal(new long[] { 4, 6 }, second.Where(e => e.Type == "match").Select(e => e.Tender!.Id));
        Assert.Equal(new long[] { 3, 4, 5, 6, 7 }, evaluator.Batches[1]);
        var controller = new CustomersController(db);
        var saved = (await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!.ToList();
        Assert.Equal(new long[] { 2, 4, 6 }, saved.Select(x => x.Tender.Id).Order().ToArray());
        Assert.All(saved, x => Assert.True(x.IsOpen));

        await controller.SetFeedback("C-1", 4, new TenderFeedbackRequest("dislike"), CancellationToken.None);
        var third = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-1", 2, Importer(db, http), CancellationToken.None)) third.Add(item);
        Assert.Equal(8, Assert.Single(third, e => e.Type == "match").Tender!.Id);
        var retained = (await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!.Select(x => x.Tender.Id).Order().ToArray();
        Assert.Equal(new long[] { 2, 6, 8 }, retained);

        var other = new List<TenderDiscoveryEvent>();
        await foreach (var item in matcher.DiscoverAsync("C-2", 1, Importer(db, http), CancellationToken.None)) other.Add(item);
        Assert.Equal(2, Assert.Single(other, e => e.Type == "match").Tender!.Id);
        Assert.Equal(1, await db.TenderFeedback.CountAsync());
    }

    [Fact]
    public async Task SavedDiscoveriesRetainExpiredMatchesButExcludeDeletedAndDisliked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.AddRange(Enumerable.Range(1, 3).Select(Tender));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 3, Importer(db, http), CancellationToken.None)) { }
        var controller = new CustomersController(db);
        var expired = await db.Tenders.SingleAsync(t => t.Id == 1);
        expired.EndDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        await controller.SetFeedback("C-1", 2, new TenderFeedbackRequest("dislike"), CancellationToken.None);
        await new TendersController(db).Delete(3, CancellationToken.None);
        var saved = Assert.Single((await controller.GetDiscoveries("C-1", CancellationToken.None)).Value!);
        Assert.Equal(1, saved.Tender.Id);
        Assert.False(saved.IsOpen);
        Assert.Empty((await controller.GetMatches("C-1", CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task InterruptedSearchKeepsDeliveredMatchesAndContinuesFromNextTender()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.AddRange(Enumerable.Range(1, 4).Select(Tender));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 3, Importer(db, http), CancellationToken.None))
            if (item.Type == "match") break;
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.TenderDiscoverySeen.CountAsync());
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 3, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(new long[] { 2, 3, 4 }, events.Where(e => e.Type == "match").Select(e => e.Tender!.Id));
        Assert.Equal(4, (await new CustomersController(db).GetDiscoveries("C-1", CancellationToken.None)).Value!.Count());
    }

    [Fact]
    public async Task LegacyMatchesStaySavedAndAreNotRediscovered()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.AddRange(Tender(1), Tender(2));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        await foreach (var _ in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) { }
        await db.TenderDiscoverySeen.ExecuteDeleteAsync();
        var noMatch = new MatchingService(db, new NoMatchEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        await foreach (var _ in noMatch.RunStreamingAsync(CancellationToken.None, "C-1")) { }
        Assert.Equal("1", Assert.Single((await new CustomersController(db).GetMatches("C-1", CancellationToken.None)).Value!).LotId);
        var next = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) next.Add(item);
        Assert.Equal(2, Assert.Single(next, e => e.Type == "match").Tender!.Id);
    }

    [Fact]
    public async Task FailedEvaluationDoesNotAdvanceTheDiscoveryHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(Customer("C-1"));
        db.Tenders.Add(Tender(1));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var failed = new MatchingService(db, new FailingEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        await Assert.ThrowsAsync<TenderMatchEvaluationException>(async () =>
        {
            await foreach (var _ in failed.DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) { }
        });
        Assert.Empty(await db.TenderDiscoverySeen.ToListAsync());
        var next = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) next.Add(item);
        Assert.Equal(1, Assert.Single(next, e => e.Type == "match").Tender!.Id);
    }

    [Fact]
    public async Task CustomerEditsAndMatchingPromptChangesReconsiderNonmatchesButKeepSavedMatches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenderAiDbContext>().UseSqlite(connection).Options;
        await using var db = new TenderAiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(Customer("C-1"), Customer("C-2"));
        db.Tenders.AddRange(Enumerable.Range(1, 3).Select(Tender));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new UzexHandler(0)) { BaseAddress = new Uri("https://uzex.test/") };
        var noMatch = new MatchingService(db, new NoMatchEvaluator(), TimeProvider.System, NullLogger<MatchingService>.Instance);
        await foreach (var _ in noMatch.DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) { }
        await foreach (var _ in noMatch.DiscoverAsync("C-2", 1, Importer(db, http), CancellationToken.None)) { }
        Assert.Equal(6, await db.TenderDiscoverySeen.CountAsync());

        var customers = new CustomersController(db);
        var record = (await customers.Get("C-1", CancellationToken.None)).Value!;
        record.Customer!.Name = "Updated bakery";
        await customers.Update("C-1", record, CancellationToken.None);
        Assert.Equal(0, await db.TenderDiscoverySeen.CountAsync(s => s.CustomerId == "C-1"));
        Assert.Equal(3, await db.TenderDiscoverySeen.CountAsync(s => s.CustomerId == "C-2"));
        var events = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) events.Add(item);
        Assert.Equal(1, Assert.Single(events, e => e.Type == "match").Tender!.Id);

        var prompts = new PromptsController(db);
        await prompts.Update("summarization", new("New summary prompt", 0), CancellationToken.None);
        Assert.Equal(4, await db.TenderDiscoverySeen.CountAsync());
        await prompts.Update("matching", new("New matching prompt", 0), CancellationToken.None);
        Assert.Equal(0, await db.TenderDiscoverySeen.CountAsync());
        var next = new List<TenderDiscoveryEvent>();
        await foreach (var item in Matcher(db).DiscoverAsync("C-1", 1, Importer(db, http), CancellationToken.None)) next.Add(item);
        Assert.Equal(2, Assert.Single(next, e => e.Type == "match").Tender!.Id);
        Assert.Equal(2, (await customers.GetDiscoveries("C-1", CancellationToken.None)).Value!.Count());
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
        CurrencyCode = "UZS", StartPrice = 100 + id, EndDate = DateTime.UtcNow.AddDays(10)
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

    private sealed class EvenMatchEvaluator : ITenderMatchEvaluator
    {
        public List<long[]> Batches { get; } = [];
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct)
        {
            Batches.Add(tenders.Select(t => long.Parse(t.TenderId)).ToArray());
            return Task.FromResult<IReadOnlyList<TenderCandidateMatch>>(tenders
                .Where(t => long.Parse(t.TenderId) % 2 == 0)
                .Select(t => new TenderCandidateMatch(t.TenderId, "Bread activity and incoming bread payments match.")).ToList());
        }

    }

    private sealed class FailingEvaluator : ITenderMatchEvaluator
    {
        public Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated evaluation failure");
    }

    private sealed class Summarizer : ITenderSummarizer
    {
        public Task<string> SummarizeAsync(TenderEntity tender, CancellationToken ct)
        {
            tender.EnglishTitle = "Bread supply";
            return Task.FromResult("Supply bread.");
        }
    }

    private sealed class UzexHandler(int count, bool failDetails = false) : HttpMessageHandler
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
                    end_date = DateTime.UtcNow.AddDays(10), cost = 100 + i, currency_codeabc = "UZS"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(rows) });
            }
            if (failDetails)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { name = "Bread supply", customer_name = "Buyer" })
            });
        }
    }
}
