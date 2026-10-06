using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class ProofOfDeliveryFormTests
{
    private static List<ValidationResult> Validate(ProofOfDeliveryForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void New_IsCleanWithTheSampleCmr() =>
        new ProofOfDeliveryForm().Should().Match<ProofOfDeliveryForm>(form => form.Condition == ProofOfDeliveryCondition.Clean && form.UseSampleCmr);

    [Fact]
    public void Validate_WithoutReceivedBy_SaysSo() =>
        Validate(new ProofOfDeliveryForm()).Should().ContainSingle().Which.ErrorMessage.Should().Be(ProofOfDeliveryForm.ReceivedByRequired);

    [Fact]
    public void Validate_CleanWithoutARemark_IsValid() =>
        Validate(new ProofOfDeliveryForm { ReceivedBy = "J. Jansen" }).Should().BeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validate_ReservationsWithoutARemark_AsksForOne(string? remark) =>
        Validate(new ProofOfDeliveryForm { ReceivedBy = "J. Jansen", Condition = ProofOfDeliveryCondition.Reservations, Remark = remark })
            .Should().ContainSingle().Which.Should().Match<ValidationResult>(
                result => result.ErrorMessage == ProofOfDeliveryForm.RemarkRequired && result.MemberNames.Contains("Remark"));

    [Fact]
    public void Validate_ReservationsWithARemark_IsValid() =>
        Validate(new ProofOfDeliveryForm { ReceivedBy = "J. Jansen", Condition = ProofOfDeliveryCondition.Reservations, Remark = "Two pallets damaged" }).Should().BeEmpty();

    [Fact]
    public void Validate_ARemarkOverTheLimit_IsInvalid() =>
        Validate(new ProofOfDeliveryForm { ReceivedBy = "J. Jansen", Remark = new string('x', ProofOfDeliveryForm.RemarkMaxLength + 1) }).Should().ContainSingle();

    [Fact]
    public void ConditionName_BindsTheCondition_ByName()
    {
        var form = new ProofOfDeliveryForm { ConditionName = "Reservations" };

        (form.Condition, form.ConditionName).Should().Be((ProofOfDeliveryCondition.Reservations, "Reservations"));
    }
}
