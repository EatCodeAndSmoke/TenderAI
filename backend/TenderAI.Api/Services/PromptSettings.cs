using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Data;

namespace TenderAI.Api.Services
{
    public static class PromptDefinitions
    {
        public const string Matching = "matching";
        public const string Summarization = "summarization";

        public static string? DefaultFor(string key) => key switch
        {
            Matching => TenderMatchPrompt.Instructions,
            Summarization => TenderSummaryPrompt.Instructions,
            _ => null
        };

        public static string? RequiredFor(string key) => key switch
        {
            Matching => TenderMatchPrompt.RequiredInstructions,
            Summarization => TenderSummaryPrompt.RequiredInstructions,
            _ => null
        };
    }

    public sealed class PromptSettings(IServiceScopeFactory scopeFactory)
    {
        public async Task<string> GetInstructionsAsync(string key, CancellationToken ct)
        {
            var defaultInstructions = PromptDefinitions.DefaultFor(key)
                ?? throw new ArgumentOutOfRangeException(nameof(key));
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TenderAiDbContext>();
            var saved = await db.PromptSettings.AsNoTracking()
                .Where(prompt => prompt.Key == key)
                .Select(prompt => prompt.Instructions)
                .SingleOrDefaultAsync(ct);
            return saved ?? defaultInstructions;
        }
    }
}
