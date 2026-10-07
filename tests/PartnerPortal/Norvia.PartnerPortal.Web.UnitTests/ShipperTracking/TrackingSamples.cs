using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

internal static class TrackingSamples
{
    public static readonly Guid CmrFile = Guid.Parse("6f1d3b52-8a4e-4c9d-b7e1-2a5c9e0f4d11");

    public static ShipperTrackingSummary Summary(
        string number = "TO-0000042", string? reference = "BD-2030-0001", ShipperTrackingStatus status = ShipperTrackingStatus.InTransit,
        string? eta = "03-10-2030 14:30", string? lastUpdate = "02-10-2030 09:10") => new(
        number, reference, status, new ShipperPlace("Rotterdam", "NL"), new ShipperPlace("Düsseldorf", "DE"), eta, lastUpdate);

    public static ShipperTrackingDetail Detail(
        string number = "TO-0000042", ShipperTrackingStatus status = ShipperTrackingStatus.Delivered, bool withProof = true) => new(
        number, "BD-2030-0001", status, new ShipperPlace("Rotterdam", "NL"), "01-10-2030 08:00", "01-10-2030 12:00",
        new ShipperPlace("Düsseldorf", "DE"), "02-10-2030 09:00", "02-10-2030 13:00", "02-10-2030 10:30", "02-10-2030 10:05",
        [
            new ShipperProgressEvent(ShipperProgressType.Collected, "01-10-2030 09:30", new ShipperPlace("Rotterdam", "NL")),
            new ShipperProgressEvent(ShipperProgressType.OutForDelivery, "02-10-2030 08:00", new ShipperPlace("Düsseldorf", "DE")),
            new ShipperProgressEvent(ShipperProgressType.Delivered, "02-10-2030 10:05", new ShipperPlace("Düsseldorf", "DE")),
        ],
        withProof
            ? new ShipperProofOfDelivery("M. Müller", "02-10-2030 10:05", ProofOfDeliveryCondition.Reservations,
                [new ShipperProofFile(CmrFile, "CMR signed.pdf", "application/pdf", 48213), new ShipperProofFile(Guid.CreateVersion7(), "damage.jpg", "image/jpeg", 1_523_004)])
            : null);
}
