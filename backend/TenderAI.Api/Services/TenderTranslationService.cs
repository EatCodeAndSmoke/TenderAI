using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class TenderTranslationService(
        ITextTranslator translator,
        ILogger<TenderTranslationService> logger)
    {
        private const int MaxParallelTenders = 4;

        public async Task<int> TranslateMissingTendersAsync(TenderAiDbContext db, CancellationToken ct)
        {
            var tenders = await db.Tenders
                .Where(tender => !tender.Translations.Any(translation => translation.Lang == "en"))
                .Include(tender => tender.Products)
                    .ThenInclude(product => product.Translations)
                .AsSplitQuery()
                .ToListAsync(ct);

            await TranslateTendersAsync(tenders, ct);
            await db.SaveChangesAsync(ct);
            return tenders.Count;
        }

        public async Task TranslateTendersAsync(IReadOnlyCollection<TenderEntity> tenders, CancellationToken ct)
        {
            await Parallel.ForEachAsync(
                tenders,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelTenders,
                    CancellationToken = ct
                },
                async (tender, token) => await TranslateTenderAsync(tender, token));
        }

        private async Task TranslateTenderAsync(TenderEntity tender, CancellationToken ct)
        {
            var errors = new List<string>();
            var translatedAt = DateTime.UtcNow;
            var translation = new TenderTranslationEntity
            {
                Tender = tender,
                Lang = "en",
                TranslatedAt = translatedAt,
                Name = await TranslateFieldAsync(tender.Name, "Name", errors, ct),
                BuyerName = await TranslateFieldAsync(tender.BuyerName, "BuyerName", errors, ct),
                RegionName = await TranslateFieldAsync(tender.RegionName, "RegionName", errors, ct),
                DistrictName = await TranslateFieldAsync(tender.DistrictName, "DistrictName", errors, ct),
                ProcedureType = await TranslateFieldAsync(tender.ProcedureType, "ProcedureType", errors, ct),
                EvaluationMethod = await TranslateFieldAsync(tender.EvaluationMethod, "EvaluationMethod", errors, ct),
                PaymentType = await TranslateFieldAsync(tender.PaymentType, "PaymentType", errors, ct),
                DepositName = await TranslateFieldAsync(tender.DepositName, "DepositName", errors, ct),
                FinancingSource = await TranslateFieldAsync(tender.FinancingSource, "FinancingSource", errors, ct),
                DeliveryRegion = await TranslateFieldAsync(tender.DeliveryRegion, "DeliveryRegion", errors, ct),
                DeliveryDistrict = await TranslateFieldAsync(tender.DeliveryDistrict, "DeliveryDistrict", errors, ct),
                DeliveryAddress = await TranslateFieldAsync(tender.DeliveryAddress, "DeliveryAddress", errors, ct),
                Description = await TranslateFieldAsync(tender.Description, "Description", errors, ct),
                Requirements = []
            };

            for (var index = 0; index < tender.Requirements.Count; index++)
            {
                var original = tender.Requirements[index];
                var translated = await TranslateFieldAsync(original, $"Requirements[{index}]", errors, ct);
                translation.Requirements.Add(translated ?? original);
            }

            foreach (var product in tender.Products)
            {
                if (product.Translations.Any(translation => translation.Lang == "en"))
                {
                    continue;
                }

                var prefix = $"Products[{product.OrderNum}]";
                product.Translations.Add(new TenderProductTranslationEntity
                {
                    Product = product,
                    Lang = "en",
                    TranslatedAt = translatedAt,
                    CategoryName = await TranslateFieldAsync(product.CategoryName, $"{prefix}.CategoryName", errors, ct),
                    ProductName = await TranslateFieldAsync(product.ProductName, $"{prefix}.ProductName", errors, ct),
                    Description = await TranslateFieldAsync(product.Description, $"{prefix}.Description", errors, ct),
                    Unit = await TranslateFieldAsync(product.Unit, $"{prefix}.Unit", errors, ct),
                    WarrantyTermUnit = await TranslateFieldAsync(product.WarrantyTermUnit, $"{prefix}.WarrantyTermUnit", errors, ct)
                });
            }

            tender.Translations.Add(translation);
            tender.TranslationError = errors.Count == 0 ? null : string.Join("; ", errors);

            if (tender.TranslationError is not null)
            {
                logger.LogError(
                    "English translation failed for UZEX tender {TenderId}: {TranslationError}",
                    tender.Id,
                    tender.TranslationError);
            }
        }

        private async Task<string?> TranslateFieldAsync(
            string? text,
            string field,
            ICollection<string> errors,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return await translator.TranslateAsync(text, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                errors.Add($"{field}: {ex.Message}");
                return null;
            }
        }
    }
}
