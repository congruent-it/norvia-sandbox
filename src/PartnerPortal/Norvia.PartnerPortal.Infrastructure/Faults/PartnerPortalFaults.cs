using Norvia.PartnerPortal.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

internal sealed class PartnerPortalFaults(IFaults faults) : IPartnerPortalFaults
{
    public bool TrackingUnmappedCode => faults.IsOn(PartnerPortalFaultNames.TrackingUnmappedCode);

    public bool AnswerUnmappedReason => faults.IsOn(PartnerPortalFaultNames.AnswerUnmappedReason);

    public bool BookingEndpointDown => faults.IsOn(PartnerPortalFaultNames.BookingEndpointDown);

    public bool ProofHeicPhoto => faults.IsOn(PartnerPortalFaultNames.ProofHeicPhoto);

    public bool ShipperEndpointDown => faults.IsOn(PartnerPortalFaultNames.ShipperEndpointDown);

    public bool CancellationEndpointDown => faults.IsOn(PartnerPortalFaultNames.CancellationEndpointDown);
}
