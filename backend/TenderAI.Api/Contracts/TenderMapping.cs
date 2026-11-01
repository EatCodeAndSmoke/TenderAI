using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Contracts
{
    public static class TenderMapping
    {
        public static TenderDto ToDto(this TenderEntity t) => new(
            t.Id,
            t.DisplayNo,
            t.Source,
            t.SourceUrl,
            t.Name,
            t.BuyerName,
            t.BuyerTin,
            t.RegionName,
            t.DistrictName,
            t.StartDate,
            t.EndDate,
            t.StartPrice,
            t.CurrencyCode,
            t.ProcedureType,
            t.EvaluationMethod,
            t.PaymentType,
            t.AdvancePaymentPercent,
            t.PaymentTermDays,
            t.DepositName,
            t.DepositPercent,
            t.DepositPercent is { } percent ? Math.Round(t.StartPrice * percent / 100, 2) : null,
            t.BankGuaranteeAccepted,
            t.FinancingSource,
            t.DeliveryRegion,
            t.DeliveryDistrict,
            t.DeliveryAddress,
            t.Description,
            t.Requirements,
            t.BidLanguages,
            // SQLite doesn't keep DateTimeKind; the value is stored in UTC
            DateTime.SpecifyKind(t.LoadedAt, DateTimeKind.Utc),
            t.Products.OrderBy(p => p.OrderNum).ThenBy(p => p.Id).Select(ToDto).ToList(),
            t.Translations.ToDictionary(tr => tr.Lang, tr => new TenderTextDto(
                tr.Name,
                tr.BuyerName,
                tr.RegionName,
                tr.DistrictName,
                tr.ProcedureType,
                tr.EvaluationMethod,
                tr.PaymentType,
                tr.DepositName,
                tr.FinancingSource,
                tr.DeliveryRegion,
                tr.DeliveryDistrict,
                tr.DeliveryAddress,
                tr.Description,
                tr.Requirements)));

        private static TenderProductDto ToDto(TenderProductEntity p) => new(
            p.Id,
            p.OrderNum,
            p.ProductCode,
            p.CategoryName,
            p.ProductName,
            p.Description,
            p.Quantity,
            p.Unit,
            p.Price,
            p.Cost,
            p.DeliveryTermDays,
            p.WarrantyTerm,
            p.WarrantyTermUnit,
            p.Translations.ToDictionary(tr => tr.Lang, tr => new TenderProductTextDto(
                tr.CategoryName,
                tr.ProductName,
                tr.Description,
                tr.Unit,
                tr.WarrantyTermUnit)));
    }
}
