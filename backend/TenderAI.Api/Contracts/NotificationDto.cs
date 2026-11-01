namespace TenderAI.Api.Contracts
{
    // JSON shape used by the banking app's notification cards
    public record NotificationDto(
        string Id,
        string LotId,
        DateTime CreatedAt,
        string Title,
        string Body,
        string Buyer,
        string SourceUrl,
        string Region,
        decimal StartPrice,
        string CurrencyCode,
        DateOnly Deadline,
        int? PaymentTermsDays,
        IReadOnlyList<string> Requirements,
        string MatchReason,
        string LoanDecisionReason,
        LoanOfferDto? Offer);

    public record LoanOfferDto(long Financing, long Guarantee, long PreApprovedLimit);

    public record MatchDto(
        string CustomerId,
        string CustomerName,
        string LotId,
        string TenderTitle,
        decimal StartPrice,
        string CurrencyCode,
        string MatchReason,
        string LoanDecisionReason,
        LoanOfferDto? Offer,
        DateTime CreatedAt);
}
