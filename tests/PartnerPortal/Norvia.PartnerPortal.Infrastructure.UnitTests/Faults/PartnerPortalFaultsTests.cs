using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Infrastructure.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Faults;

public sealed class PartnerPortalFaultsTests
{
    private readonly IFaults _state = Substitute.For<IFaults>();

    [Fact]
    public void EachFault_AsksTheSwitchboardForItsOwnName()
    {
        var faults = new PartnerPortalFaults(_state);
        _state.IsOn(PartnerPortalFaultNames.TrackingUnmappedCode).Returns(true);

        faults.TrackingUnmappedCode.Should().BeTrue();
        faults.AnswerUnmappedReason.Should().BeFalse();
        faults.BookingEndpointDown.Should().BeFalse();
        faults.ProofHeicPhoto.Should().BeFalse();
        faults.ShipperEndpointDown.Should().BeFalse();
        faults.CancellationEndpointDown.Should().BeFalse();
    }

    [Fact]
    public void Definitions_AreOwnedByThePortal_WithUniqueNames_AndEveryNameIsDefined()
    {
        PartnerPortalFaultDefinitions.All.Should().OnlyContain(definition => definition.Owner == "partner-portal");
        PartnerPortalFaultDefinitions.All.Select(definition => definition.Name).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(
                PartnerPortalFaultNames.TrackingUnmappedCode, PartnerPortalFaultNames.AnswerUnmappedReason, PartnerPortalFaultNames.BookingEndpointDown,
                PartnerPortalFaultNames.ProofHeicPhoto, PartnerPortalFaultNames.ShipperEndpointDown, PartnerPortalFaultNames.CancellationEndpointDown, PartnerPortalFaultNames.DriverReportsLogicAppDisabled);
    }
}
