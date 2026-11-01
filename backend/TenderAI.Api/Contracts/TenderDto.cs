namespace TenderAI.Api.Contracts
{
    // A tender as the admin app shows it. Text fields are the original UZEX text; Translations holds
    // other languages by code ("en", ...). A missing language or a null field means "use the original".
    public record TenderDto(
        long Id,
        string DisplayNo,
        string Source,
        string SourceUrl,
        string Name,
        string BuyerName,
        string? BuyerTin,
        string? RegionName,
        string? DistrictName,
        // Tashkent local time, without an offset
        DateTime StartDate,
        DateTime EndDate,
        decimal StartPrice,
        string CurrencyCode,
        string? ProcedureType,
        string? EvaluationMethod,
        string? PaymentType,
        decimal? AdvancePaymentPercent,
        int? PaymentTermDays,
        string? DepositName,
        decimal? DepositPercent,
        decimal? DepositAmount,
        bool BankGuaranteeAccepted,
        string? FinancingSource,
        string? DeliveryRegion,
        string? DeliveryDistrict,
        string? DeliveryAddress,
        string? Description,
        IReadOnlyList<string> Requirements,
        IReadOnlyList<string> BidLanguages,
        DateTime LoadedAt,
        IReadOnlyList<TenderProductDto> Products,
        IReadOnlyDictionary<string, TenderTextDto> Translations,
        string? TranslationError);

    public record TenderTextDto(
        string? Name,
        string? BuyerName,
        string? RegionName,
        string? DistrictName,
        string? ProcedureType,
        string? EvaluationMethod,
        string? PaymentType,
        string? DepositName,
        string? FinancingSource,
        string? DeliveryRegion,
        string? DeliveryDistrict,
        string? DeliveryAddress,
        string? Description,
        IReadOnlyList<string> Requirements);

    public record TenderProductDto(
        int Id,
        int OrderNum,
        string? ProductCode,
        string? CategoryName,
        string ProductName,
        string? Description,
        decimal Quantity,
        string? Unit,
        decimal Price,
        decimal Cost,
        int? DeliveryTermDays,
        int? WarrantyTerm,
        string? WarrantyTermUnit,
        IReadOnlyDictionary<string, TenderProductTextDto> Translations);

    public record TenderProductTextDto(
        string? CategoryName,
        string? ProductName,
        string? Description,
        string? Unit,
        string? WarrantyTermUnit);
}
