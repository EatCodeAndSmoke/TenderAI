using Amazon;
using Amazon.BedrockRuntime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderAI.Api.Data;
using TenderAI.Api.Services;
using TenderAI.Api.Services.Uzex;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<TenderAiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TenderAi")));

// Tenders come from UZEX; Bedrock creates a short, plain-English description from the original text.
builder.Services.Configure<UzexOptions>(builder.Configuration.GetSection(UzexOptions.Section));
builder.Services.Configure<BedrockOptions>(builder.Configuration.GetSection(BedrockOptions.Section));
builder.Services.AddHttpClient<UzexClient>((services, http) =>
{
    var uzex = services.GetRequiredService<IOptions<UzexOptions>>().Value;
    http.BaseAddress = new Uri(uzex.ApiBaseUrl);
    http.Timeout = TimeSpan.FromSeconds(uzex.TimeoutSeconds);
});
builder.Services.AddScoped<TenderImportService>();
builder.Services.AddScoped<CustomerGenerationService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddSingleton<PromptSettings>();
builder.Services.AddSingleton<ITenderMatchEvaluator, BedrockTenderMatchEvaluator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAmazonBedrockRuntime>(services =>
{
    var region = services.GetRequiredService<IOptions<BedrockOptions>>().Value.Region;
    region = string.IsNullOrWhiteSpace(region)
        ? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
        : region;

    return string.IsNullOrWhiteSpace(region)
        ? new AmazonBedrockRuntimeClient()
        : new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(region)
        });
});
builder.Services.AddSingleton<ICustomerProfileGenerator, BedrockCustomerProfileGenerator>();
builder.Services.AddSingleton<ITenderSummarizer, BedrockTenderSummarizer>();
builder.Services.AddSingleton<TenderSummaryService>();

var app = builder.Build();

// Create the SQLite database on first run. Business records are loaded through the application.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
    db.Database.EnsureCreated();

    DatabaseSchema.AddMissingTables(db);
    DatabaseSchema.AddMissingColumns(db);
    DatabaseSchema.RemoveKnownDemoRecords(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();