using Norvia.PartnerPortal.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

internal static class PartnerPortalFaultDefinitions
{
    public const string Owner = "partner-portal";

    public static readonly FaultDefinition TrackingUnmappedCode = new(
        PartnerPortalFaultNames.TrackingUnmappedCode,
        Owner,
        "A Carrier sends Delivered as DLV instead of POD: the Carrier-tracking Logic App can't map it and dead-letters the message, and the Carrier is still told 202.");

    public static readonly FaultDefinition AnswerUnmappedReason = new(
        PartnerPortalFaultNames.AnswerUnmappedReason,
        Owner,
        "A Carrier declines with a reason code the Carrier-answer Logic App can't map: the run fails and the Carrier hears the answer is unreadable.");

    public static readonly FaultDefinition BookingEndpointDown = new(
        PartnerPortalFaultNames.BookingEndpointDown,
        Owner,
        "Every Carrier's booking endpoint answers 503: the booking ends Not delivered.");

    public static readonly FaultDefinition ProofHeicPhoto = new(
        PartnerPortalFaultNames.ProofHeicPhoto,
        Owner,
        "A Carrier attaches a .heic photo to its Proof of delivery: the TMS refuses it with FileNotAccepted and the message is dead-lettered.");

    public static readonly FaultDefinition ShipperEndpointDown = new(
        PartnerPortalFaultNames.ShipperEndpointDown,
        Owner,
        "Every Shipper's notification endpoint answers 503: the notification ends Not delivered.");

    public static readonly FaultDefinition CancellationEndpointDown = new(
        PartnerPortalFaultNames.CancellationEndpointDown,
        Owner,
        "Every Carrier's cancellation endpoint answers 503: the Carrier booking is flagged Carrier not told.");

    public static readonly FaultDefinition[] All =
    [
        TrackingUnmappedCode, AnswerUnmappedReason, BookingEndpointDown, ProofHeicPhoto, ShipperEndpointDown, CancellationEndpointDown,
    ];
}
