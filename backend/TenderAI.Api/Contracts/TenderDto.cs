namespace TenderAI.Api.Contracts
{
    // A tender as the admin app shows it. Original UZEX text stays in storage; only its English
    // summary is presented here.
    public record TenderDto(
        long Id,
        string DisplayNo,
        string Source,
        string SourceUrl,
        string? EnglishTitle,
        string? Summary,
        string? SummaryError,
        decimal StartPrice,
        string CurrencyCode,
        decimal? DepositPercent,
        decimal? DepositAmount,
        decimal? AdvancePaymentPercent,
        int? PaymentTermDays,
        DateTime EndDate);
}
