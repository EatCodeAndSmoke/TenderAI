using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services.Uzex;

namespace TenderAI.Api.Services
{
    // Loads new tenders from UZEX into the database
    public class TenderImportService(
        TenderAiDbContext db,
        UzexClient uzex,
        TenderTranslationService translator,
        IOptions<UzexOptions> options,
        ILogger<TenderImportService> logger)
    {
        private const int ListPageSize = 50;
        private const int MaxParallelDetails = 4;

        // One load at a time, so two clicks can't save the same lot twice
        private static readonly SemaphoreSlim LoadLock = new(1, 1);

        // Saves up to count lots that aren't in the database yet and returns them.
        // Walks the UZEX list page by page and skips lots that are already loaded.
        // A lot whose details can't be read is skipped, and the next new one is taken instead.
        public async Task<List<TenderEntity>> LoadNewAsync(int count, CancellationToken ct)
        {
            await LoadLock.WaitAsync(ct);
            try
            {
                var translatedExisting = await translator.TranslateMissingTendersAsync(db, ct);
                if (translatedExisting > 0)
                {
                    logger.LogInformation(
                        "Backfilled English translations for {Count} existing tenders",
                        translatedExisting);
                }

                var known = (await db.Tenders.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
                var loaded = new List<TenderEntity>();
                var loadedAt = DateTime.UtcNow;

                for (int from = 1, total = int.MaxValue; loaded.Count < count && from <= total; from += ListPageSize)
                {
                    var page = await uzex.GetTradeListAsync(from, from + ListPageSize - 1, ct);
                    if (page.Count == 0) break;
                    total = page[0].TotalCount ?? 0;

                    // known.Add is false for lots in the database and for repeats within the list
                    var candidates = new Queue<UzexTradeListItem>(page.Where(item => known.Add(item.Id)));
                    while (candidates.Count > 0 && loaded.Count < count)
                    {
                        var batch = new List<UzexTradeListItem>();
                        while (candidates.Count > 0 && batch.Count < count - loaded.Count)
                        {
                            batch.Add(candidates.Dequeue());
                        }

                        var tenders = await ReadDetailsAsync(batch, loadedAt, ct);
                        var successfulDetails = tenders.OfType<TenderEntity>().ToList();
                        await translator.TranslateTendersAsync(successfulDetails, ct);
                        loaded.AddRange(successfulDetails);
                    }
                }

                db.Tenders.AddRange(loaded);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Loaded {Count} new tenders from UZEX", loaded.Count);
                return loaded;
            }
            finally
            {
                LoadLock.Release();
            }
        }

        // Details take about a second per lot, so a few are read at the same time.
        // The result keeps the list order and has null for lots that failed.
        private async Task<TenderEntity?[]> ReadDetailsAsync(List<UzexTradeListItem> batch, DateTime loadedAt, CancellationToken ct)
        {
            using var throttle = new SemaphoreSlim(MaxParallelDetails);
            return await Task.WhenAll(batch.Select(async item =>
            {
                await throttle.WaitAsync(ct);
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
                finally
                {
                    throttle.Release();
                }
            }));
        }
    }
}
