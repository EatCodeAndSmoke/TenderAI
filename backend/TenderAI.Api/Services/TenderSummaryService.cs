using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class TenderSummaryService(
        ITenderSummarizer summarizer,
        ILogger<TenderSummaryService> logger)
    {
        private const int MaxParallelTenders = 5;
        public const int CurrentSummaryVersion = 4;

        public async Task<int> SummarizeMissingTendersAsync(TenderAiDbContext db, CancellationToken ct)
        {
            var tenders = await db.Tenders
                .Where(tender => !tender.IsDeleted && (tender.Summary == null
                    || tender.Summary == ""
                    || tender.SummaryVersion != CurrentSummaryVersion))
                .Include(tender => tender.Products)
                .AsSplitQuery()
                .ToListAsync(ct);

            await SummarizeTendersAsync(tenders, ct);
            await db.SaveChangesAsync(ct);
            return tenders.Count;
        }

        public async Task SummarizeTendersAsync(IReadOnlyCollection<TenderEntity> tenders, CancellationToken ct)
        {
            await Parallel.ForEachAsync(
                tenders,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelTenders,
                    CancellationToken = ct
                },
                async (tender, token) => await SummarizeTenderAsync(tender, token));
        }

        private async Task SummarizeTenderAsync(TenderEntity tender, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(tender.Summary)
                && tender.SummaryVersion == CurrentSummaryVersion)
            {
                return;
            }

            try
            {
                tender.Summary = await summarizer.SummarizeAsync(tender, ct);
                tender.SummaryVersion = CurrentSummaryVersion;
                tender.SummaryError = null;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                tender.SummaryError = ex.Message;
                logger.LogError(
                    ex,
                    "English summary failed for UZEX tender {TenderId}",
                    tender.Id);
            }
        }
    }
}
