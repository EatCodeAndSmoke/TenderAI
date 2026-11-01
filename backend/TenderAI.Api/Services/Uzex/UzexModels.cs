using System.Text.Json;

namespace TenderAI.Api.Services.Uzex
{
    // The parts of the UZEX e-tender API responses that TenderAI uses. UZEX names fields in snake_case
    // (start_cost) and Pascal_Snake_Case inside budget_products (Product_Name); the client reads both
    // with a case-insensitive snake_case policy.

    // One row of POST api/common/TradeList
    public class UzexTradeListItem
    {
        public long Id { get; set; }
        public string? DisplayNo { get; set; }

        // Number of lots the list has in total
        public int? TotalCount { get; set; }

        public string? Name { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Cost { get; set; }
        public string? SellerName { get; set; }
        public string? SellerTin { get; set; }
        public string? RegionName { get; set; }
        public string? DistrictName { get; set; }
        public string? CurrencyCodeabc { get; set; }
    }

    // GET api/common/GetTrade/{id}/0
    public class UzexTrade
    {
        public long Id { get; set; }
        public string? DisplayNo { get; set; }
        public string? Name { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? StartCost { get; set; }
        public string? CurrencyCodeabc { get; set; }

        public string? TypeName { get; set; }
        public string? ValuationName { get; set; }

        public string? PaymentTypeName { get; set; }
        public decimal? AdvancePaymentPerc { get; set; }
        public int? TermPaymentDays { get; set; }

        public string? PledgeName { get; set; }

        // Percent of the start price
        public decimal? PledgeValue { get; set; }

        // 1 when a bank guarantee is accepted
        public int? IsBankGuarantee { get; set; }

        public string? FinancingSource { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerTin { get; set; }
        public string? CustomerRegionName { get; set; }

        public string? DeliveringRegionName { get; set; }
        public string? DeliveringDistrictName { get; set; }
        public string? DeliveringAddress { get; set; }

        public string? Description { get; set; }
        public string? AddonDescription { get; set; }

        // These come either as JSON arrays or as strings that contain a JSON array
        public JsonElement BudgetProducts { get; set; }
        public JsonElement JsQualificationFields { get; set; }
        public JsonElement Languages { get; set; }
    }

    public class UzexBudgetProduct
    {
        public int? OrderNum { get; set; }
        public string? CategoryName { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? Cost { get; set; }
        public int? DeliveryTerm { get; set; }
        public int? GuaranteeTerm { get; set; }
        public string? TermPeriodName { get; set; }
        public JsonElement JsProperties { get; set; }
    }

    public class UzexProductProperty
    {
        public string? PropertyName { get; set; }
        public string? UserValue { get; set; }
    }

    public class UzexQualification
    {
        public int? OrderNum { get; set; }
        public string? Name { get; set; }
    }

    public class UzexLanguage
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
