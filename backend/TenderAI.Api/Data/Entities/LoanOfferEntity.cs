namespace TenderAI.Api.Data.Entities
{
    // Pre-approved loan offer attached to a notification. Stored in the Notifications table. Amounts are in so'm.
    public class LoanOfferEntity
    {
        // Contract financing up to this amount
        public long Financing { get; set; }

        // Bid guarantee needed to submit the bid
        public long Guarantee { get; set; }

        public long PreApprovedLimit { get; set; }
    }
}
