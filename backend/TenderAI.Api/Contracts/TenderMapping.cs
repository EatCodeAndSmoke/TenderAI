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
            t.EnglishTitle,
            t.Summary,
            t.SummaryError,
            t.StartPrice,
            t.CurrencyCode,
            t.DepositPercent,
            t.DepositPercent is { } percent ? Math.Round(t.StartPrice * percent / 100, 2) : null,
            t.AdvancePaymentPercent,
            t.PaymentTermDays,
            t.EndDate);
    }
}
