using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public static class DemoAccounts
    {
        public static List<CustomerAccountEntity> Create(string customerId)
        {
            // Fictional Georgian IBAN, shared by all three currency balances.
            var number = Random.Shared.NextInt64(0, 10_000_000_000_000_000L).ToString("D16");
            var remainder = 0;
            foreach (var digit in $"2911{number}161400") remainder = (remainder * 10 + digit - '0') % 97;
            var iban = $"GE{98 - remainder:D2}TB{number}";
            return new[] { "GEL", "USD", "EUR" }.Select(currency => new CustomerAccountEntity
            {
                CustomerId = customerId,
                Iban = iban,
                Currency = currency,
                Balance = Random.Shared.Next(5_000, 250_000) + Random.Shared.Next(0, 100) / 100m
            }).ToList();
        }
    }
}
