namespace TenderAI.Api.Data.Entities
{
    // A tender product's text fields in another language. A null field isn't translated yet.
    public class TenderProductTranslationEntity
    {
        public int ProductId { get; set; }
        public TenderProductEntity? Product { get; set; }

        public required string Lang { get; set; }

        public string? CategoryName { get; set; }
        public string? ProductName { get; set; }
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public string? WarrantyTermUnit { get; set; }

        // UTC
        public DateTime TranslatedAt { get; set; }
    }
}
