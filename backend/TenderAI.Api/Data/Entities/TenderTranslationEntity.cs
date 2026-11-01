namespace TenderAI.Api.Data.Entities
{
    // A tender's text fields in another language (e.g. "en"). A null field isn't translated yet,
    // so readers fall back to the original text on TenderEntity.
    public class TenderTranslationEntity
    {
        public long TenderId { get; set; }
        public TenderEntity? Tender { get; set; }

        // en, ru, ...
        public required string Lang { get; set; }

        public string? Name { get; set; }
        public string? BuyerName { get; set; }
        public string? RegionName { get; set; }
        public string? DistrictName { get; set; }
        public string? ProcedureType { get; set; }
        public string? EvaluationMethod { get; set; }
        public string? PaymentType { get; set; }
        public string? DepositName { get; set; }
        public string? FinancingSource { get; set; }
        public string? DeliveryRegion { get; set; }
        public string? DeliveryDistrict { get; set; }
        public string? DeliveryAddress { get; set; }
        public string? Description { get; set; }

        // Same order as TenderEntity.Requirements. Empty when not translated yet. Stored as a JSON array.
        public List<string> Requirements { get; set; } = [];

        // UTC
        public DateTime TranslatedAt { get; set; }
    }
}
