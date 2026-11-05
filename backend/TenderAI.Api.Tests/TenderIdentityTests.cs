using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;
using Xunit;

namespace TenderAI.Api.Tests;

public class TenderIdentityTests
{
    [Fact]
    public void IdentityIgnoresLotIdsFormattingAndProductOrderButIncludesKeyTerms()
    {
        var first = Create(1);
        var duplicate = Create(2);
        duplicate.BuyerName = "  BUYER  ";
        duplicate.Name = " Equipment   supply ";
        duplicate.StartPrice = 100.00m;
        duplicate.Products.Reverse();
        duplicate.Products[0].Quantity = 2.00m;
        Assert.Equal(TenderIdentity.For(first), TenderIdentity.For(duplicate));

        duplicate.StartPrice++;
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(duplicate));
        duplicate.StartPrice--;
        duplicate.Products[0].Quantity++;
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(duplicate));
        duplicate.Products[0].Quantity--;
        duplicate.EndDate = duplicate.EndDate.AddDays(1);
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(duplicate));
        duplicate.EndDate = first.EndDate;
        duplicate.BuyerName = "Other buyer";
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(duplicate));
        duplicate.BuyerName = "";
        Assert.Null(TenderIdentity.For(duplicate));
    }

    [Fact]
    public void IdentityGroupsReportedLotTimesButKeepsDifferentDatesAndProductsSeparate()
    {
        var first = Create(520629);
        first.StartDate = new DateTime(2026, 10, 20, 11, 59, 28);
        first.EndDate = new DateTime(2026, 11, 5, 11, 59, 28);
        var second = Create(520630);
        second.StartDate = new DateTime(2026, 10, 20, 11, 59, 59);
        second.EndDate = new DateTime(2026, 11, 5, 11, 59, 59);
        var third = Create(520631);
        third.StartDate = new DateTime(2026, 10, 20, 12, 0, 41);
        third.EndDate = new DateTime(2026, 11, 5, 12, 0, 41);

        Assert.Equal(TenderIdentity.For(first), TenderIdentity.For(second));
        Assert.Equal(TenderIdentity.For(first), TenderIdentity.For(third));

        third.StartDate = third.StartDate.AddDays(1);
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(third));
        third.StartDate = first.StartDate;
        third.EndDate = third.EndDate.AddDays(1);
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(third));
        third.EndDate = first.EndDate;
        third.Products[0].ProductName = "Other product";
        Assert.NotEqual(TenderIdentity.For(first), TenderIdentity.For(third));
    }

    private static TenderEntity Create(long id) => new()
    {
        Id = id, DisplayNo = $"LOT-{id}", Source = "UZEX", SourceUrl = $"https://example.test/{id}",
        Name = "Equipment supply", BuyerName = "Buyer", CurrencyCode = "UZS", StartPrice = 100m,
        EndDate = new DateTime(2026, 12, 1, 12, 0, 0),
        Products =
        [
            new TenderProductEntity { ProductName = "Computers", Quantity = 1, Unit = "piece" },
            new TenderProductEntity { ProductName = "Screens", Quantity = 2, Unit = "piece" }
        ]
    };
}
