namespace TenderAI.Api.Data.Entities
{
    // A business customer of the bank. CustomerId (C-01, C-02, ...) is the primary key.
    public class CustomerEntity
    {
        public required string CustomerId { get; set; }
        public string Name { get; set; } = "";

        // TIN (STIR), 9 digits
        public string TaxCode { get; set; } = "";

        // Legal form: LLC, Sole proprietor, JSC, Private enterprise
        public string Type { get; set; } = "";

        // OKED activity
        public string IndustryCode { get; set; } = "";
        public string IndustryName { get; set; } = "";

        public string RegionName { get; set; } = "";
        public string DistrictName { get; set; } = "";

        // Notifications are written in this language: en, uz or ru
        public string LanguageCode { get; set; } = "";

        public DateOnly? RegistrationDate { get; set; }
        public DateOnly CustomerSince { get; set; }

        // On the government's list of unreliable suppliers (banned from state tenders)
        public bool OnRegister { get; set; }

        public CustomerRiskFeaturesEntity? RiskFeatures { get; set; }
        public List<CustomerPaymentEntity> Payments { get; set; } = [];
        public List<NotificationEntity> Notifications { get; set; } = [];
    }
}
