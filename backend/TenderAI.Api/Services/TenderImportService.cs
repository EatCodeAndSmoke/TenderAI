using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services.Uzex;

namespace TenderAI.Api.Services
{
    // Loads new tenders from UZEX into the database
    public class TenderImportService(
        TenderAiDbContext db,
        UzexClient uzex,
        TenderSummaryService summarizer,
        IOptions<UzexOptions> options,
        ILogger<TenderImportService> logger)
    {
        private const int ListPageSize = 50;
        public const int MaxTendersPerLoad = 5;

        // One load at a time, so two clicks can't save the same lot twice
        private static readonly SemaphoreSlim LoadLock = new(1, 1);

        // Saves up to count lots that aren't in the database yet and returns them.
        // Walks the UZEX list page by page and skips lots that are already loaded.
        // A lot whose details can't be read is skipped, and the next new one is taken instead.
        public async Task<List<TenderEntity>> LoadNewAsync(int count, CancellationToken ct)
        {
            var loaded = new List<TenderEntity>();
            await foreach (var tender in LoadNewStreamingAsync(count, ct)) loaded.Add(tender);
            return loaded;
        }

        public async Task RefreshSummaryAsync(TenderEntity tender, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(tender.EnglishTitle)) tender.SummaryVersion = null;
            await summarizer.SummarizeTendersAsync([tender], ct);
            await db.Tenders.Where(t => t.Id == tender.Id && !t.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Summary, tender.Summary)
                    .SetProperty(t => t.EnglishTitle, tender.EnglishTitle)
                    .SetProperty(t => t.SummaryVersion, tender.SummaryVersion)
                    .SetProperty(t => t.SummaryError, tender.SummaryError), ct);
        }

        public async IAsyncEnumerable<TenderEntity> LoadNewStreamingAsync(int count, [EnumeratorCancellation] CancellationToken ct)
        {
            count = Math.Clamp(count, 1, MaxTendersPerLoad);
            await LoadLock.WaitAsync(ct);
            try
            {
                var summarizedExisting = await summarizer.SummarizeMissingTendersAsync(db, ct);
                if (summarizedExisting > 0)
                {
                    logger.LogInformation(
                        "Backfilled English summaries for {Count} existing tenders",
                        summarizedExisting);
                }

                var known = (await db.Tenders.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
                var loaded = 0;
                var loadedAt = DateTime.UtcNow;

                for (int from = 1, total = int.MaxValue; loaded < count && from <= total; from += ListPageSize)
                {
                    var page = await uzex.GetTradeListAsync(from, from + ListPageSize - 1, ct);
                    if (page.Count == 0) break;
                    total = page[0].TotalCount ?? 0;

                    // known.Add is false for lots in the database and for repeats within the list
                    var candidates = new Queue<UzexTradeListItem>(page.Where(item => known.Add(item.Id)));
                    while (candidates.Count > 0 && loaded < count)
                    {
                        var tender = await ReadDetailsAsync(candidates.Dequeue(), loadedAt, ct);
                        if (tender is null) continue;
                        await summarizer.SummarizeTendersAsync([tender], ct);
                        db.Tenders.Add(tender);
                        await db.SaveChangesAsync(ct);
                        loaded++;
                        yield return tender;
                    }
                }

                logger.LogInformation("Loaded {Count} new tenders from UZEX", loaded);
            }
            finally
            {
                LoadLock.Release();
            }
        }

        private async Task<TenderEntity?> ReadDetailsAsync(UzexTradeListItem item, DateTime loadedAt, CancellationToken ct)
        {
            try
            {
                var details = await uzex.GetTradeAsync(item.Id, ct);
                var sourceUrl = string.Format(options.Value.LotUrl, item.Id);
                return UzexMapping.ToTenderEntity(item, details, sourceUrl, loadedAt);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Skipped UZEX lot {LotId}: its details couldn't be read", item.Id);
                return null;
            }
        }
    }
}
