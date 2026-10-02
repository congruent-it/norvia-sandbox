using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierAnswerSettingsTests
{
    private static readonly CarrierAnswerPolicy Confirming = new(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, TimeSpan.FromSeconds(3));

    [Fact]
    public void Choose_KeepsThePolicyForThePersona()
    {
        var settings = CarrierAnswerSettings.Choose(new PersonaId("kessler-spedition"), Confirming);

        settings.PersonaId.Value.Should().Be("kessler-spedition");
        settings.Policy.Should().Be(Confirming);
    }

    [Fact]
    public void Change_ReplacesThePolicy()
    {
        var settings = CarrierAnswerSettings.Choose(new PersonaId("kessler-spedition"), Confirming);
        var rejecting = new CarrierAnswerPolicy(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, TimeSpan.Zero);

        settings.Change(rejecting);

        settings.Policy.Should().Be(rejecting);
    }
}
