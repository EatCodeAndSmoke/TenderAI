using System.ComponentModel.DataAnnotations;

namespace TenderAI.Api.Contracts
{
    // JSON shape shared with the admin app: { customer, riskFeatures, recentPayments, onRegister }

    public class CustomerRecordDto
    {
        [Required]
        public CustomerDto? Customer { get; set; }

        [Required]
        public RiskFeaturesDto? RiskFeatures { get; set; }

        public List<PaymentDto> RecentPayments { get; set; } = [];

        public bool OnRegister { get; set; }
    }

    public class CustomerDto
    {
        [Required, MaxLength(20)]
        public string CustomerId { get; set; } = "";

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

        [Required, RegularExpression(@"^\d{9}$", ErrorMessage = "The TIN must be 9 digits.")]
        public string TaxCode { get; set; } = "";

        [Required, MaxLength(50)]
        public string Type { get; set; } = "";

        [Required]
        public IndustryDto? Industry { get; set; }

        [Required]
        public AddressDto? Address { get; set; }

        [Required, RegularExpression("^(en|uz|ru)$", ErrorMessage = "The language must be en, uz or ru.")]
        public string LanguageCode { get; set; } = "";

        public DateOnly? RegistrationDate { get; set; }

        [Required]
        public DateOnly? CustomerSince { get; set; }
    }

    public class IndustryDto
    {
        [Required, MaxLength(10)]
        public string IndustryCode { get; set; } = "";

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";
    }

    public class AddressDto
    {
        [Required, MaxLength(100)]
        public string RegionName { get; set; } = "";

        [MaxLength(100)]
        public string? DistrictName { get; set; }
    }

    public class RiskFeaturesDto
    {
        // Ignored on input: the customer ID comes from the customer object
        public string? CustomerId { get; set; }

        [Required, Range(0, long.MaxValue)]
        public long? AvgMonthlyInflow6m { get; set; }

        // Ignored on input: always calculated from customer.customerSince
        public int? MonthsWithBank { get; set; }

        [Range(0, long.MaxValue)]
        public long OutstandingAmount { get; set; }

        [Range(0, long.MaxValue)]
        public long OverdueAmount { get; set; }

        [Range(0, int.MaxValue)]
        public int MaxOverdueDays12m { get; set; }

        [Range(0, long.MaxValue)]
        public long? PreApprovedLimit { get; set; }

        public bool KycPassed { get; set; }
    }

    // TransactionHistoryItem
    public class PaymentDto
    {
        [Required]
        public DateOnly? Date { get; set; }

        public bool IsIncoming { get; set; }

        [Required, MaxLength(500)]
        public string PurposeDescription { get; set; } = "";

        [Range(0, long.MaxValue)]
        public long? Amount { get; set; }
    }
}
