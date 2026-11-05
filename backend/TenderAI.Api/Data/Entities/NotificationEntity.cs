namespace TenderAI.Api.Data.Entities
{
    // A tender match notification shown in the Business Bank app.
    public class NotificationEntity
    {
        // N-101, N-102, ...
        public required string Id { get; set; }

        public required string CustomerId { get; set; }
        public CustomerEntity? Customer { get; set; }

        // The tender lot this notification is about (T-1001, ...)
        public required string LotId { get; set; }

        // UTC
        public DateTime CreatedAt { get; set; }

        public required string Title { get; set; }
        public required string Body { get; set; }
        public required string Buyer { get; set; }
        public required string SourceUrl { get; set; }
        public required string Region { get; set; }

        public decimal StartPrice { get; set; }
        public required string CurrencyCode { get; set; }
        public DateOnly Deadline { get; set; }
        public int PaymentTermsDays { get; set; }

        // What the buyer needs. Stored as a JSON array.
        public List<string> Requirements { get; set; } = [];

        public required string MatchReason { get; set; }
        public int? MatchScore { get; set; }
        public required string LoanDecisionReason { get; set; }

        // Kept populated for existing SQLite databases where this legacy NOT NULL column remains.
        public string WhyText { get; set; } = "";

        // Only present when the loan advisor made an offer
        public LoanOfferEntity? Offer { get; set; }
    }
}
