namespace TenderAI.Api.Data.Entities
{
    public class CustomerAccountEntity
    {
        public int Id { get; set; }
        public required string CustomerId { get; set; }
        public required string Iban { get; set; }
        public required string Currency { get; set; }
        public decimal Balance { get; set; }
        public CustomerEntity Customer { get; set; } = null!;
    }
}
