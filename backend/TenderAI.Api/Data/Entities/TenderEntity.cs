namespace TenderAI.Api.Data.Entities
{
    // A tender lot loaded from UZEX. Source text stays intact; Summary is the generated plain-English
    // explanation shown in the admin app.
    public class TenderEntity
    {
        // The UZEX lot ID, e.g. 511680. Also used to skip lots that are already loaded.
        public long Id { get; set; }

        // UZEX lot number shown on the website, e.g. 26111006511680
        public required string DisplayNo { get; set; }

        public required string Source { get; set; }
        public required string SourceUrl { get; set; }

        public required string Name { get; set; }

        // The organization that buys
        public required string BuyerName { get; set; }
        public string? BuyerTin { get; set; }
        public string? RegionName { get; set; }
        public string? DistrictName { get; set; }

        // Tashkent local time, as UZEX publishes it
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public decimal StartPrice { get; set; }

        // ISO code: UZS, USD, ...
        public required string CurrencyCode { get; set; }

        // Procedure: "Tender", "Konkurs", ... and how bids are evaluated
        public string? ProcedureType { get; set; }
        public string? EvaluationMethod { get; set; }

        // Payment terms
        public string? PaymentType { get; set; }
        public decimal? AdvancePaymentPercent { get; set; }
        public int? PaymentTermDays { get; set; }

        // Bid deposit (guarantee), as a percent of the start price, and whether a bank guarantee is accepted for it
        public string? DepositName { get; set; }
        public decimal? DepositPercent { get; set; }
        public bool BankGuaranteeAccepted { get; set; }

        public string? FinancingSource { get; set; }

        // Where the goods or services go
        public string? DeliveryRegion { get; set; }
        public string? DeliveryDistrict { get; set; }
        public string? DeliveryAddress { get; set; }

        public string? Description { get; set; }

        // Qualification requirements for bidders. Stored as a JSON array.
        public List<string> Requirements { get; set; } = [];

        // Languages bids can be submitted in: ru, uz, uz-Cyrl, en. Stored as a JSON array.
        public List<string> BidLanguages { get; set; } = [];

        // UTC
        public DateTime LoadedAt { get; set; }

        public string? Summary { get; set; }
        public string? SummaryError { get; set; }

        public List<TenderProductEntity> Products { get; set; } = [];
    }
}
