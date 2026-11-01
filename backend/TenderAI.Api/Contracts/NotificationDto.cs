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
        string Region,
        long StartPrice,
        DateOnly Deadline,
        int PaymentTermsDays,
        IReadOnlyList<string> Requirements,
        string WhyText,
        LoanOfferDto? Offer);

    public record LoanOfferDto(long Financing, long Guarantee, long PreApprovedLimit);
}
