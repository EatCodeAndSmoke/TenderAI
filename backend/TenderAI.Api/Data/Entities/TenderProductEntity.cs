namespace TenderAI.Api.Data.Entities
{
    // One line of what the buyer wants to purchase in a tender
    public class TenderProductEntity
    {
        public int Id { get; set; }

        public long TenderId { get; set; }
        public TenderEntity? Tender { get; set; }

        public int OrderNum { get; set; }

        // Product classifier code, e.g. 21.32.28.110-00001. The first part is the activity code
        // that can be compared with a customer's industry code.
        public string? ProductCode { get; set; }
        public string? CategoryName { get; set; }
        public required string ProductName { get; set; }
        public string? Description { get; set; }

        public decimal Quantity { get; set; }
        public string? Unit { get; set; }

        // In the tender's currency
        public decimal Price { get; set; }
        public decimal Cost { get; set; }

        public int? DeliveryTermDays { get; set; }

        // Warranty, e.g. 12 months
        public int? WarrantyTerm { get; set; }
        public string? WarrantyTermUnit { get; set; }

    }
}
