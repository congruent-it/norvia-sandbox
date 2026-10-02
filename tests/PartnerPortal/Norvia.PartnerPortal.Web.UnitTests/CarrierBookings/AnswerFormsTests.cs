using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class AnswerFormsTests
{
    private static List<ValidationResult> Validate(object form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ConfirmForm_WithoutAReference_IsInvalid(string? reference)
    {
        var results = Validate(new ConfirmForm { BookingReference = reference });

        results.Should().ContainSingle().Which.ErrorMessage.Should().Be(ConfirmForm.ReferenceRequired);
    }

    [Fact]
    public void ConfirmForm_WithAReference_BecomesAConfirmation_WithTheTimeInRotterdamsOffset()
    {
        var form = new ConfirmForm { BookingReference = " VDT-77812 ", PlannedCollection = new DateTime(2030, 10, 2, 8, 30, 0) };

        Validate(form).Should().BeEmpty();
        var answer = form.ToAnswer();
        answer.BookingReference.Should().Be("VDT-77812");
        answer.PlannedCollection.Should().Be(new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void ConfirmForm_WithoutAPlannedTime_HasNone()
    {
        new ConfirmForm { BookingReference = "R-1" }.ToAnswer().PlannedCollection.Should().BeNull();
    }

    [Fact]
    public void ConfirmForm_APlannedTimeInWinter_UsesTheWinterOffset()
    {
        new ConfirmForm { BookingReference = "R-1", PlannedCollection = new DateTime(2030, 12, 2, 8, 30, 0) }.ToAnswer().PlannedCollection!.Value.Offset
            .Should().Be(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData(CarrierRejectionReason.NoCapacity)]
    [InlineData(CarrierRejectionReason.LaneNotServed)]
    [InlineData(CarrierRejectionReason.WindowNotFeasible)]
    [InlineData(CarrierRejectionReason.GoodsNotAccepted)]
    public void RejectForm_ForAReasonOtherThanOther_NeedsNoText(CarrierRejectionReason reason)
    {
        var form = new RejectForm { Reason = reason };

        Validate(form).Should().BeEmpty();
        form.ToAnswer().Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectForm_ForOtherWithoutText_IsInvalid_AndNamesTheTextField(string? text)
    {
        var results = Validate(new RejectForm { Reason = CarrierRejectionReason.Other, Text = text });

        var problem = results.Should().ContainSingle().Subject;
        problem.ErrorMessage.Should().Be(RejectForm.TextRequired);
        problem.MemberNames.Should().Equal(nameof(RejectForm.Text));
    }

    [Fact]
    public void RejectForm_ForOtherWithText_BecomesARejection()
    {
        var form = new RejectForm { Reason = CarrierRejectionReason.Other, Text = "No trucks on Friday" };

        Validate(form).Should().BeEmpty();
        form.ToAnswer().Text.Should().Be("No trucks on Friday");
    }

    [Fact]
    public void RejectForm_WithATextOverTheLimit_IsInvalid()
    {
        Validate(new RejectForm { Text = new string('x', RejectForm.TextMaxLength + 1) }).Should().ContainSingle();
    }
}
