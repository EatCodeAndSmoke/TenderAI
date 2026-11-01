namespace TenderAI.Api.Data.Entities
{
    // Loan and risk data for one customer (one-to-one with CustomerEntity). Amounts are in so'm.
    // Months with the bank isn't stored: it is calculated from CustomerEntity.CustomerSince.
    public class CustomerRiskFeaturesEntity
    {
        public required string CustomerId { get; set; }
        public CustomerEntity? Customer { get; set; }

        public long AvgMonthlyInflow6m { get; set; }
        public long OutstandingAmount { get; set; }
        public long OverdueAmount { get; set; }

        // From the KATM credit bureau report
        public int MaxOverdueDays12m { get; set; }

        // Null when the customer has no pre-approved limit
        public long? PreApprovedLimit { get; set; }

        public bool KycPassed { get; set; }
    }
}
