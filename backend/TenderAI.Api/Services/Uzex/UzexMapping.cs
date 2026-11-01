using System.Text.RegularExpressions;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services.Uzex
{
    public static partial class UzexMapping
    {
        private const string UnitProperty = "Единица измерения";

        // UZEX language IDs for bid documents
        private static readonly Dictionary<int, string> LanguageCodes = new()
        {
            [1] = "ru",
            [2] = "uz",
            [3] = "en",
            [4] = "uz-Cyrl"
        };

        // Builds a tender from its row in the trade list and its details
        public static TenderEntity ToTenderEntity(UzexTradeListItem item, UzexTrade d, string sourceUrl, DateTime loadedAtUtc)
        {
            var products = UzexClient.ReadList<UzexBudgetProduct>(d.BudgetProducts);
            var requirements = UzexClient.ReadList<UzexQualification>(d.JsQualificationFields)
                .OrderBy(q => q.OrderNum)
                .Select(q => Clean(q.Name))
                .OfType<string>()
                .Distinct()
                .ToList();
            var languages = UzexClient.ReadList<UzexLanguage>(d.Languages)
                .Select(l => LanguageCodes.GetValueOrDefault(l.Id) ?? Clean(l.Name))
                .OfType<string>()
                .Distinct()
                .ToList();

            return new TenderEntity
            {
                Id = item.Id,
                DisplayNo = Clean(item.DisplayNo) ?? Clean(d.DisplayNo) ?? item.Id.ToString(),
                Source = "UZEX",
                SourceUrl = sourceUrl,
                Name = Clean(item.Name) ?? Clean(d.Name) ?? $"Lot {item.Id}",
                BuyerName = Clean(item.SellerName) ?? Clean(d.CustomerName) ?? "",
                BuyerTin = Clean(item.SellerTin) ?? Clean(d.CustomerTin),
                RegionName = Clean(item.RegionName) ?? Clean(d.CustomerRegionName),
                DistrictName = Clean(item.DistrictName),
                StartDate = item.StartDate ?? d.StartDate ?? default,
                EndDate = item.EndDate ?? d.EndDate ?? default,
                StartPrice = item.Cost ?? d.StartCost ?? 0,
                CurrencyCode = Clean(item.CurrencyCodeabc) ?? Clean(d.CurrencyCodeabc) ?? "UZS",
                ProcedureType = Clean(d.TypeName),
                EvaluationMethod = Clean(d.ValuationName),
                PaymentType = Clean(d.PaymentTypeName),
                AdvancePaymentPercent = d.AdvancePaymentPerc,
                PaymentTermDays = d.TermPaymentDays,
                DepositName = Clean(d.PledgeName),
                DepositPercent = d.PledgeValue,
                BankGuaranteeAccepted = d.IsBankGuarantee == 1,
                FinancingSource = Clean(d.FinancingSource),
                DeliveryRegion = Clean(d.DeliveringRegionName),
                DeliveryDistrict = Clean(d.DeliveringDistrictName),
                DeliveryAddress = Clean(d.DeliveringAddress),
                Description = d.Description?.Trim() is { Length: > 0 } description ? description : d.AddonDescription?.Trim(),
                Requirements = requirements,
                BidLanguages = languages,
                LoadedAt = loadedAtUtc,
                Products = products
                    .OrderBy(p => p.OrderNum)
                    .Select((p, i) => new TenderProductEntity
                    {
                        TenderId = item.Id,
                        OrderNum = p.OrderNum ?? i + 1,
                        ProductCode = Clean(p.ProductCode),
                        CategoryName = Clean(p.CategoryName),
                        ProductName = Clean(p.ProductName) ?? Clean(p.Description) ?? "",
                        Description = Clean(p.Description),
                        Quantity = p.Quantity ?? 0,
                        Unit = Clean(UzexClient.ReadList<UzexProductProperty>(p.JsProperties)
                            .FirstOrDefault(prop => prop.PropertyName == UnitProperty)?.UserValue),
                        Price = p.Price ?? 0,
                        Cost = p.Cost ?? 0,
                        DeliveryTermDays = p.DeliveryTerm,
                        WarrantyTerm = p.GuaranteeTerm,
                        WarrantyTermUnit = Clean(p.TermPeriodName)
                    })
                    .ToList()
            };
        }

        // Trims and joins runs of spaces and line breaks; empty text becomes null
        private static string? Clean(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : Whitespace().Replace(text.Trim(), " ");

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
