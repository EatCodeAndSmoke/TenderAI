using System.Text.Json;
using System.Text.RegularExpressions;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services.Uzex
{
    public static partial class UzexMapping
    {
        private const string UnitProperty = "Единица измерения";
        private static readonly string[] RequirementNameKeys =
            ["property_name", "field_name", "js_field_name", "question", "title", "label", "name", "caption"];
        private static readonly string[] RequirementValueKeys =
            ["user_value", "field_value", "js_field_value", "answer", "value", "text", "description", "content"];
        private static readonly string[] BuyerRequirementMarkers =
        [
            "cipm", "ilac", "calibrat", "калибров", "калибр", "метролог",
            "train", "training", "обуч", "ўқитиш", "o'qit",
            "manufactur", "year of", "год выпуска", "производств", "ишлаб чиқарилган",
            "install", "commission", "trial operation", "пусконалад", "монтаж", "установк", "ишга тушириш",
            "packag", "упаков", "transport", "транспорт", "перевоз", "ташиш",
            "deliver", "delivery", "ddp", "достав", "поставк", "етказиб бериш",
            "warrant", "guarantee", "гаранти", "кафолат",
            "technical assignment", "technical specification", "техническ", "техник топшириқ"
        ];
        private static readonly string[] BidderAdministrativeMarkers =
        [
            "bidder", "tenderer", "qualification", "eligibility", "experience", "registration",
            "license", "licence", "tax debt", "solvency", "scoring", "score", "points", "evaluation criteria",
            "заявител", "квалификац", "опыт работы", "лиценз", "регистрац", "налог", "балл", "оценк",
            "malaka", "tajriba", "litsenziya", "ro'yxat", "soliq", "ball", "baholash"
        ];
        private static readonly string[] BidderAdministrativeValueMarkers =
        [
            "bidder qualification", "supplier qualification", "years of experience", "tax debt",
            "tax arrears", "eligibility criteria", "evaluation points", "company registration",
            "квалификац", "опыт работы участника", "лицензия участника", "налоговая задолженность",
            "ishtirokchi malakasi", "soliq qarzi"
        ];

        // UZEX language IDs for bid documents
        private static readonly Dictionary<int, string> LanguageCodes = new()
        {
            [1] = "ru",
            [2] = "uz",
            [3] = "en",
            [4] = "uz-Cyrl"
        };

        // Builds a tender from its row in the trade list and its details
        public static TenderEntity ToTenderEntity(UzexTradeListItem item, UzexTrade d, string sourceUrl, DateTime loadedAtUtc)
        {
            var products = UzexClient.ReadList<UzexBudgetProduct>(d.BudgetProducts);
            var requirements = UzexClient.ReadList<UzexQualification>(d.JsQualificationFields)
                .OrderBy(q => q.OrderNum)
                .Select(q => Clean(q.Name))
                .OfType<string>()
                .Distinct()
                .ToList();
            var languages = UzexClient.ReadList<UzexLanguage>(d.Languages)
                .Select(l => LanguageCodes.GetValueOrDefault(l.Id) ?? Clean(l.Name))
                .OfType<string>()
                .Distinct()
                .ToList();
            var buyerRequirements = ExtractBuyerRequirements(d.JsFields);

            return new TenderEntity
            {
                Id = item.Id,
                DisplayNo = Clean(item.DisplayNo) ?? Clean(d.DisplayNo) ?? item.Id.ToString(),
                Source = "UZEX",
                SourceUrl = sourceUrl,
                Name = Clean(item.Name) ?? Clean(d.Name) ?? $"Lot {item.Id}",
                DetailedName = Clean(d.Name),
                BuyerName = Clean(item.SellerName) ?? Clean(d.CustomerName) ?? "",
                BuyerTin = Clean(item.SellerTin) ?? Clean(d.CustomerTin),
                RegionName = Clean(item.RegionName) ?? Clean(d.CustomerRegionName),
                DistrictName = Clean(item.DistrictName),
                StartDate = item.StartDate ?? d.StartDate ?? default,
                EndDate = item.EndDate ?? d.EndDate ?? default,
                StartPrice = item.Cost ?? d.StartCost ?? 0,
                CurrencyCode = Clean(item.CurrencyCodeabc) ?? Clean(d.CurrencyCodeabc) ?? "UZS",
                ProcedureType = Clean(d.TypeName),
                EvaluationMethod = Clean(d.ValuationName),
                PaymentType = Clean(d.PaymentTypeName),
                AdvancePaymentPercent = d.AdvancePaymentPerc,
                PaymentTermDays = d.TermPaymentDays,
                DepositName = Clean(d.PledgeName),
                DepositPercent = d.PledgeValue,
                BankGuaranteeAccepted = d.IsBankGuarantee == 1,
                FinancingSource = Clean(d.FinancingSource),
                DeliveryRegion = Clean(d.DeliveringRegionName),
                DeliveryDistrict = Clean(d.DeliveringDistrictName),
                DeliveryAddress = Clean(d.DeliveringAddress),
                Description = Clean(d.Description),
                AdditionalDescription = Clean(d.AddonDescription),
                HasUnparsedTechnicalDocuments =
                    !string.IsNullOrWhiteSpace(d.TechFilePath) || !string.IsNullOrWhiteSpace(d.TechDocFilePath),
                Requirements = requirements,
                BuyerRequirements = buyerRequirements,
                BidLanguages = languages,
                LoadedAt = loadedAtUtc,
                Products = products
                    .OrderBy(p => p.OrderNum)
                    .Select((p, i) =>
                    {
                        var properties = UzexClient.ReadList<UzexProductProperty>(p.JsProperties);
                        return new TenderProductEntity
                        {
                            TenderId = item.Id,
                            OrderNum = p.OrderNum ?? i + 1,
                            ProductCode = Clean(p.ProductCode),
                            CategoryName = Clean(p.CategoryName),
                            ProductName = Clean(p.ProductName) ?? Clean(p.Description) ?? "",
                            Description = Clean(p.Description),
                            TechnicalSpecifications = FormatProperties(properties),
                            Quantity = p.Quantity ?? 0,
                            Unit = Clean(properties.FirstOrDefault(prop => prop.PropertyName == UnitProperty)?.UserValue),
                            Price = p.Price ?? 0,
                            Cost = p.Cost ?? 0,
                            DeliveryTermDays = p.DeliveryTerm,
                            WarrantyTerm = p.GuaranteeTerm,
                            WarrantyTermUnit = Clean(p.TermPeriodName)
                        };
                    })
                    .ToList()
            };
        }

        private static List<string> ExtractBuyerRequirements(JsonElement source)
        {
            var requirements = new List<string>();
            foreach (var field in ReadJsonEntries(source))
            {
                var name = field.ValueKind == JsonValueKind.Object
                    ? ReadJsonText(field, RequirementNameKeys)
                    : null;
                var value = ReadJsonText(field, RequirementValueKeys);
                var text = string.Join(": ", new[] { name, value }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var normalized = text.ToLowerInvariant();
                var normalizedName = (name ?? "").ToLowerInvariant();
                if (BidderAdministrativeMarkers.Any(normalizedName.Contains)
                    || BidderAdministrativeValueMarkers.Any(normalized.Contains)
                    || !BuyerRequirementMarkers.Any(normalized.Contains))
                {
                    continue;
                }

                var cleaned = Clean(text);
                if (cleaned is not null && !requirements.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                {
                    requirements.Add(cleaned);
                }
            }

            return requirements;
        }

        private static List<JsonElement> ReadJsonEntries(JsonElement source)
        {
            if (source.ValueKind == JsonValueKind.String)
            {
                var json = source.GetString();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return [];
                }

                using var document = JsonDocument.Parse(json);
                return ReadJsonEntries(document.RootElement);
            }

            if (source.ValueKind == JsonValueKind.Array)
            {
                return source.EnumerateArray().Select(value => value.Clone()).ToList();
            }

            if (source.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            foreach (var property in source.EnumerateObject())
            {
                var key = NormalizeKey(property.Name);
                if (key is "fields" or "items" or "data")
                {
                    var entries = ReadJsonEntries(property.Value);
                    if (entries.Count > 0)
                    {
                        return entries;
                    }
                }
            }

            return [source.Clone()];
        }

        private static string? ReadJsonText(JsonElement element, IEnumerable<string> keys)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return Clean(element.GetString());
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var wanted = keys.Select(NormalizeKey).ToHashSet(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (wanted.Contains(NormalizeKey(property.Name)))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => Clean(property.Value.GetString()),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => null
                    };
                }
            }

            return null;
        }

        private static string NormalizeKey(string value) =>
            string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

        private static string? FormatProperties(IEnumerable<UzexProductProperty> properties)
        {
            var formatted = properties
                .Select(property =>
                {
                    var name = Clean(property.PropertyName);
                    var value = Clean(property.UserValue);
                    return name is null || value is null ? null : $"{name}: {value}";
                })
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return formatted.Count == 0 ? null : string.Join("\n", formatted);
        }

        // Trims and joins runs of spaces and line breaks; empty text becomes null
        private static string? Clean(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : Whitespace().Replace(text.Trim(), " ");

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
