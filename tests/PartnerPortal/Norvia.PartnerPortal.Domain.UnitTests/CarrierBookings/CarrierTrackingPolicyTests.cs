using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierTrackingPolicyTests
{
    [Fact]
    public void AutoProgressDelay_ForAutoProgress_IsTheDelay() =>
        new CarrierTrackingPolicy(TrackingMode.AutoProgress, TimeSpan.FromSeconds(3)).AutoProgressDelay.Should().Be(TimeSpan.FromSeconds(3));

    [Fact]
    public void AutoProgressDelay_ForManual_IsNone() =>
        new CarrierTrackingPolicy(TrackingMode.Manual, TimeSpan.FromSeconds(3)).AutoProgressDelay.Should().BeNull();

    [Fact]
    public void New_ANotDefinedMode_Throws() =>
        ((Action)(() => _ = new CarrierTrackingPolicy((TrackingMode)99, TimeSpan.Zero))).Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void New_ADelayOutOfRange_Throws(int seconds) =>
        ((Action)(() => _ = new CarrierTrackingPolicy(TrackingMode.AutoProgress, TimeSpan.FromSeconds(seconds)))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Settings_Choose_KeepsThePolicyForThePersona_AndChangeReplacesIt()
    {
        var settings = CarrierTrackingSettings.Choose(new PersonaId("kessler-spedition"), new CarrierTrackingPolicy(TrackingMode.AutoProgress, TimeSpan.FromSeconds(3)));
        var manual = new CarrierTrackingPolicy(TrackingMode.Manual, TimeSpan.Zero);

        settings.PersonaId.Value.Should().Be("kessler-spedition");
        settings.Change(manual);

        settings.Policy.Should().Be(manual);
    }
}
