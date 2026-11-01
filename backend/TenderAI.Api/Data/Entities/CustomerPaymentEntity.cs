namespace TenderAI.Api.Data.Entities
{
    // One payment from the customer's transaction history (TransactionHistoryItem).
    // The matcher only uses payments from the last 12 months.
    public class CustomerPaymentEntity
    {
        public int Id { get; set; }

        public required string CustomerId { get; set; }
        public CustomerEntity? Customer { get; set; }

        public DateOnly Date { get; set; }
        public bool IsIncoming { get; set; }
        public required string PurposeDescription { get; set; }

        // In so'm
        public long? Amount { get; set; }
    }
}
