using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

internal static class TrackingFixtures
{
    public static readonly ShipperTrackingDetail Detail = new(
        "TO-0000042", "BD-1", ShipperTrackingStatus.InTransit, new ShipperPlace("Rotterdam", "NL"), "01-10-2030 08:00", "01-10-2030 12:00",
        new ShipperPlace("Utrecht", "NL"), "02-10-2030 09:00", "02-10-2030 13:00", Eta: "02-10-2030 11:30", LastUpdate: "01-10-2030 14:05",
        Progress: [new ShipperProgressEvent(ShipperProgressType.Collected, "01-10-2030 09:10", new ShipperPlace("Rotterdam", "NL"))], ProofOfDelivery: null);

    public static ShipperTrackingSummary Summary(string number, string? reference = null) =>
        new(number, reference, ShipperTrackingStatus.Scheduled, new ShipperPlace("Rotterdam", "NL"), new ShipperPlace("Utrecht", "NL"), Eta: null, LastUpdate: null);

    public static readonly DateTimeOffset Now = new(2030, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static AssistantInput Input(ShipperTrackingDetail? detail = null, IReadOnlyList<ShipperTrackingSummary>? orders = null) =>
        AssistantInputBuilder.Build("Where is my order?", [], new TrackingData(detail ?? Detail, orders ?? [Summary("TO-0000042", "BD-1")], Found: null), Now);
}
