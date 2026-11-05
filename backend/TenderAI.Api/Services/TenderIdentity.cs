using System.Globalization;
using System.Text;
using System.Text.Json;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public static class TenderIdentity
    {
        public static string? For(TenderEntity tender)
        {
            var buyer = Normalize(tender.BuyerName);
            var name = Normalize(tender.Name);
            if (buyer.Length == 0 || name.Length == 0 || tender.EndDate == default
                || string.IsNullOrWhiteSpace(tender.CurrencyCode))
                return null;

            var products = tender.Products.Select(product => JsonSerializer.Serialize(new
            {
                Code = Normalize(product.ProductCode),
                Category = Normalize(product.CategoryName),
                Name = Normalize(product.ProductName),
                Description = Normalize(product.Description),
                Specifications = Normalize(product.TechnicalSpecifications),
                Quantity = Number(product.Quantity),
                Unit = Normalize(product.Unit),
                Price = Number(product.Price),
                Cost = Number(product.Cost),
                product.DeliveryTermDays,
                product.WarrantyTerm,
                WarrantyUnit = Normalize(product.WarrantyTermUnit)
            })).Order(StringComparer.Ordinal).ToArray();

            return JsonSerializer.Serialize(new
            {
                Source = Normalize(tender.Source),
                Buyer = buyer,
                BuyerTin = Normalize(tender.BuyerTin),
                Name = name,
                DetailedName = Normalize(tender.DetailedName),
                Description = Normalize(tender.Description),
                AdditionalDescription = Normalize(tender.AdditionalDescription),
                StartDate = Date(tender.StartDate),
                EndDate = Date(tender.EndDate),
                StartPrice = Number(tender.StartPrice),
                Currency = Normalize(tender.CurrencyCode),
                Procedure = Normalize(tender.ProcedureType),
                Evaluation = Normalize(tender.EvaluationMethod),
                Payment = Normalize(tender.PaymentType),
                AdvancePaymentPercent = Number(tender.AdvancePaymentPercent),
                tender.PaymentTermDays,
                Deposit = Normalize(tender.DepositName),
                DepositPercent = Number(tender.DepositPercent),
                tender.BankGuaranteeAccepted,
                Financing = Normalize(tender.FinancingSource),
                BuyerRegion = Normalize(tender.RegionName),
                BuyerDistrict = Normalize(tender.DistrictName),
                Region = Normalize(tender.DeliveryRegion),
                District = Normalize(tender.DeliveryDistrict),
                Address = Normalize(tender.DeliveryAddress),
                Requirements = tender.Requirements.Select(Normalize).Order(StringComparer.Ordinal).ToArray(),
                BuyerRequirements = tender.BuyerRequirements.Select(Normalize).Order(StringComparer.Ordinal).ToArray(),
                Languages = tender.BidLanguages.Select(Normalize).Order(StringComparer.Ordinal).ToArray(),
                tender.HasUnparsedTechnicalDocuments,
                Products = products
            });
        }

        private static string Date(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        private static string? Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);

        private static string Normalize(string? value) =>
            string.Join(" ", (value ?? "").Normalize(NormalizationForm.FormKC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
