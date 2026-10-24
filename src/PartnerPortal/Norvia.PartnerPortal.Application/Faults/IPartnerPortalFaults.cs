namespace Norvia.PartnerPortal.Application.Faults;

/// <summary>The Partner portal's runtime faults, which the presenter switches on for a few minutes. Each makes a Persona misbehave the way a real Carrier or Shipper can.</summary>
public interface IPartnerPortalFaults
{
    /// <summary>Whether the "unmapped tracking code" fault is on now.</summary>
    bool TrackingUnmappedCode { get; }

    /// <summary>Whether the "unmapped decline reason" fault is on now.</summary>
    bool AnswerUnmappedReason { get; }

    /// <summary>Whether the "Carrier booking endpoint down" fault is on now.</summary>
    bool BookingEndpointDown { get; }

    /// <summary>Whether the "HEIC photo" fault is on now.</summary>
    bool ProofHeicPhoto { get; }

    /// <summary>Whether the "Shipper notification endpoint down" fault is on now.</summary>
    bool ShipperEndpointDown { get; }

    /// <summary>Whether the "Carrier cancellation endpoint down" fault is on now.</summary>
    bool CancellationEndpointDown { get; }
}
