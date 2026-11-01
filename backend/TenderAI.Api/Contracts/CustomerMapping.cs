using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Contracts
{
    public static class CustomerMapping
    {
        public static CustomerRecordDto ToDto(this CustomerEntity c, DateOnly today)
        {
            var r = c.RiskFeatures;
            return new CustomerRecordDto
            {
                Customer = new CustomerDto
                {
                    CustomerId = c.CustomerId,
                    Name = c.Name,
                    TaxCode = c.TaxCode,
                    Type = c.Type,
                    Industry = new IndustryDto { IndustryCode = c.IndustryCode, Name = c.IndustryName },
                    Address = new AddressDto { RegionName = c.RegionName, DistrictName = c.DistrictName },
                    LanguageCode = c.LanguageCode,
                    RegistrationDate = c.RegistrationDate,
                    CustomerSince = c.CustomerSince
                },
                RiskFeatures = new RiskFeaturesDto
                {
                    CustomerId = c.CustomerId,
                    AvgMonthlyInflow6m = r?.AvgMonthlyInflow6m ?? 0,
                    MonthsWithBank = MonthsBetween(c.CustomerSince, today),
                    OutstandingAmount = r?.OutstandingAmount ?? 0,
                    OverdueAmount = r?.OverdueAmount ?? 0,
                    MaxOverdueDays12m = r?.MaxOverdueDays12m ?? 0,
                    PreApprovedLimit = r?.PreApprovedLimit,
                    KycPassed = r?.KycPassed ?? false
                },
                RecentPayments = c.Payments
                    .OrderByDescending(p => p.Date)
                    .ThenBy(p => p.Id)
                    .Select(p => new PaymentDto
                    {
                        Date = p.Date,
                        IsIncoming = p.IsIncoming,
                        PurposeDescription = p.PurposeDescription,
                        Amount = p.Amount
                    })
                    .ToList(),
                OnRegister = c.OnRegister
            };
        }

        // Copies everything except the customer ID (the key) from the request onto the entity
        public static void ApplyFrom(this CustomerEntity c, CustomerRecordDto dto)
        {
            var d = dto.Customer!;
            c.Name = d.Name.Trim();
            c.TaxCode = d.TaxCode.Trim();
            c.Type = d.Type.Trim();
            c.IndustryCode = d.Industry!.IndustryCode.Trim();
            c.IndustryName = d.Industry.Name.Trim();
            c.RegionName = d.Address!.RegionName.Trim();
            c.DistrictName = d.Address.DistrictName?.Trim() ?? "";
            c.LanguageCode = d.LanguageCode;
            c.RegistrationDate = d.RegistrationDate;
            c.CustomerSince = d.CustomerSince!.Value;
            c.OnRegister = dto.OnRegister;

            var r = dto.RiskFeatures!;
            c.RiskFeatures ??= new CustomerRiskFeaturesEntity { CustomerId = c.CustomerId };
            c.RiskFeatures.AvgMonthlyInflow6m = r.AvgMonthlyInflow6m!.Value;
            c.RiskFeatures.OutstandingAmount = r.OutstandingAmount;
            c.RiskFeatures.OverdueAmount = r.OverdueAmount;
            c.RiskFeatures.MaxOverdueDays12m = r.MaxOverdueDays12m;
            c.RiskFeatures.PreApprovedLimit = r.PreApprovedLimit;
            c.RiskFeatures.KycPassed = r.KycPassed;

            // The request holds the full list, so it replaces the stored payments
            c.Payments.Clear();
            c.Payments.AddRange(dto.RecentPayments.Select(p => new CustomerPaymentEntity
            {
                CustomerId = c.CustomerId,
                Date = p.Date!.Value,
                IsIncoming = p.IsIncoming,
                PurposeDescription = p.PurposeDescription.Trim(),
                Amount = p.Amount
            }));
        }

        public static NotificationDto ToDto(this NotificationEntity n) => new(
            n.Id,
            n.LotId,
            // SQLite doesn't keep DateTimeKind; the value is stored in UTC
            DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
            n.Title,
            n.Body,
            n.Buyer,
            n.SourceUrl,
            n.Region,
            n.StartPrice,
            n.CurrencyCode,
            n.Deadline,
            n.PaymentTermsDays > 0 ? n.PaymentTermsDays : null,
            n.Requirements,
            n.MatchReason,
            n.LoanDecisionReason,
            n.Offer is null ? null : new LoanOfferDto(n.Offer.Financing, n.Offer.Guarantee, n.Offer.PreApprovedLimit));

        public static MatchDto ToMatchDto(this NotificationEntity n, string customerName) => new(
            n.CustomerId,
            customerName,
            n.LotId,
            n.Title,
            n.StartPrice,
            n.CurrencyCode,
            n.MatchReason,
            n.LoanDecisionReason,
            n.Offer is null ? null : new LoanOfferDto(n.Offer.Financing, n.Offer.Guarantee, n.Offer.PreApprovedLimit),
            DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc));

        // Full months between two dates, the same rule as the admin app's monthsSince()
        public static int MonthsBetween(DateOnly from, DateOnly to)
        {
            var months = (to.Year - from.Year) * 12 + (to.Month - from.Month);
            if (to.Day < from.Day) months--;
            return Math.Max(0, months);
        }
    }
}
