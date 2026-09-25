using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class BookingFormTests
{
    [Fact]
    public void ToRequest_OfADraft_IsTheDraft()
    {
        var draft = Samples.Draft();

        var request = BookingForm.From(draft).ToRequest();

        request.Should().BeEquivalentTo(draft);
    }

    [Fact]
    public void ToRequest_WithADeliveryWindowAndInstructions_CarriesThem()
    {
        var form = BookingForm.From(Samples.Draft());
        form.DeliverFrom = new DateTime(2026, 9, 26, 14, 0, 0);
        form.DeliverUntil = new DateTime(2026, 9, 26, 18, 0, 0);
        form.Consignee.Email = "inkoop@dezwaan.example";
        form.Instructions = "Ring at the side entrance";

        var request = form.ToRequest();

        request.DeliveryWindow.Should().Be(new LocalWindow(new DateTime(2026, 9, 26, 14, 0, 0), new DateTime(2026, 9, 26, 18, 0, 0)));
        request.Consignee.Email.Should().Be("inkoop@dezwaan.example");
        request.Instructions.Should().Be("Ring at the side entrance");
    }

    [Fact]
    public void ToRequest_GoodsLineWithoutDimensions_HasNone()
    {
        var form = BookingForm.From(Samples.Draft());
        form.GoodsLines[0].LengthCm = null;
        form.GoodsLines[0].WidthCm = null;
        form.GoodsLines[0].HeightCm = null;

        form.ToRequest().GoodsLines.Single().DimensionsCm.Should().BeNull();
    }

    [Fact]
    public void AddGoodsLine_AddsAnEmptyLine_AfterTheOthers()
    {
        var form = BookingForm.From(Samples.Draft());

        form.AddGoodsLine();

        form.GoodsLines.Should().HaveCount(2);
        form.ToRequest().GoodsLines[1].Should().Be(new BookingGoodsLine(PackageType.Pallet, null, null));
    }

    [Fact]
    public void RemoveGoodsLine_RemovesThatLine()
    {
        var form = BookingForm.From(Samples.Draft());
        form.AddGoodsLine();
        var first = form.GoodsLines[0];

        form.RemoveGoodsLine(first);

        form.GoodsLines.Should().ContainSingle().Which.Should().NotBeSameAs(first);
    }

    [Theory]
    [InlineData("pallet", PackageType.Pallet)]
    [InlineData("parcel", PackageType.Parcel)]
    [InlineData("crate", PackageType.Crate)]
    [InlineData("loose", PackageType.Loose)]
    [InlineData("", null)]
    public void ToRequest_PackageTypeChosenByName_IsThatPackageType(string chosen, PackageType? packageType)
    {
        var form = BookingForm.From(Samples.Draft());
        form.GoodsLines[0].PackageType = chosen;

        form.ToRequest().GoodsLines[0].PackageType.Should().Be(packageType);
    }
}
