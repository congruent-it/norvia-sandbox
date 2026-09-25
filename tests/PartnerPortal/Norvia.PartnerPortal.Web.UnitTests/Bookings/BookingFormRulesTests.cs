using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

/// <summary>
/// The form checks only what the Norvia Partner API's format needs; which fields a booking must have, and what they may
/// hold, is for Norvia to judge, and its answer names every reason.
/// </summary>
public sealed class BookingFormRulesTests
{
    [Fact]
    public void Check_TheDraft_FindsNothing()
    {
        BookingFormRules.Check(BookingForm.From(Samples.Draft())).Should().BeEmpty();
    }

    [Fact]
    public void Check_AnEmptyForm_FindsNothing_ForNorviaToJudge()
    {
        var form = BookingForm.From(new());
        form.AddGoodsLine();

        BookingFormRules.Check(form).Should().BeEmpty();
    }

    [Fact]
    public void Check_AReferenceLongerThanThePartnerFormatAllows_IsFound()
    {
        var form = BookingForm.From(Samples.Draft());
        form.ShipperReference = new string('R', 101);

        BookingFormRules.Check(form).Should().ContainSingle()
            .Which.Should().Be(new FormProblem(form, nameof(BookingForm.ShipperReference), "The Shipper reference can be at most 100 characters."));
    }

    [Fact]
    public void Check_AnEmailAddressThatIsNone_IsFound()
    {
        var form = BookingForm.From(Samples.Draft());
        form.Consignee.Email = "not an email";

        BookingFormRules.Check(form).Should().ContainSingle()
            .Which.Should().Be(new FormProblem(form.Consignee, nameof(ConsigneeForm.Email), "Enter an email address, such as name@example.com."));
    }

    [Fact]
    public void Check_ANegativeWeight_IsFound()
    {
        var form = BookingForm.From(Samples.Draft());
        form.GoodsLines[0].WeightKg = -1;

        BookingFormRules.Check(form).Should().ContainSingle()
            .Which.Should().Be(new FormProblem(form.GoodsLines[0], nameof(GoodsLineForm.WeightKg), "The weight can't be negative."));
    }

    [Fact]
    public void Check_OnlySomeDimensions_IsFound_BecausethePartnerFormatTakesAllThreeOrNone()
    {
        var form = BookingForm.From(Samples.Draft());
        form.GoodsLines[0].HeightCm = null;

        BookingFormRules.Check(form).Should().ContainSingle()
            .Which.Should().Be(new FormProblem(form.GoodsLines[0], nameof(GoodsLineForm.LengthCm), "Give the length, width and height, or none of them."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Check_ADimensionThatIsntPositive_IsFound(decimal height)
    {
        var form = BookingForm.From(Samples.Draft());
        form.GoodsLines[0].HeightCm = height;

        BookingFormRules.Check(form).Should().ContainSingle()
            .Which.Should().Be(new FormProblem(form.GoodsLines[0], nameof(GoodsLineForm.HeightCm), "Each dimension must be more than 0 cm."));
    }
}
