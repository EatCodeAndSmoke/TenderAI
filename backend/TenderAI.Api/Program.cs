using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;
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

// Tenders come from the UZEX e-tender platform
builder.Services.Configure<UzexOptions>(builder.Configuration.GetSection(UzexOptions.Section));
builder.Services.AddHttpClient<UzexClient>((services, http) =>
{
    var uzex = services.GetRequiredService<IOptions<UzexOptions>>().Value;
    http.BaseAddress = new Uri(uzex.ApiBaseUrl);
    http.Timeout = TimeSpan.FromSeconds(uzex.TimeoutSeconds);
});
builder.Services.AddScoped<TenderImportService>();

var app = builder.Build();

// Create the SQLite database on first run and fill it with demo data.
// Delete the .db file to start again from the seed data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
    if (db.Database.EnsureCreated())
    {
        SeedData.Seed(db);
    }
    else
    {
        DatabaseSchema.AddMissingTables(db);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();