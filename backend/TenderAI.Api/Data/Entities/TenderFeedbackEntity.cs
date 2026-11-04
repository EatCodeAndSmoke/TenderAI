namespace TenderAI.Api.Data.Entities
{
    public class TenderFeedbackEntity
    {
        public required string CustomerId { get; set; }
        public long TenderId { get; set; }
        public required string Value { get; set; }
    }
}
