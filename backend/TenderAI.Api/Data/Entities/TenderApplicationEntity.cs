namespace TenderAI.Api.Data.Entities
{
    public class TenderApplicationEntity
    {
        public required string CustomerId { get; set; }
        public long TenderId { get; set; }
        public DateTime AppliedAt { get; set; }
        public string? SimulatedOutcome { get; set; }
        public DateTime? NotificationReadAt { get; set; }
    }
}
