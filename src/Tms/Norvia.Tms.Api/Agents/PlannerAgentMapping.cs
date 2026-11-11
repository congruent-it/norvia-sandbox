using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Planner agent's view of a Transport Order and its assignment options (<c>get_assignment_options</c>), field by field: places as city,
/// postcode area and country, goods as a weight and a pallet count, the windows, the Shipper's business name and the Rating result. No street
/// address, no postcode beyond its area, no Consignee name or contact, no instructions and no file. PlannerAgentViewTests holds the list.
/// </summary>
/// <param name="AsOf">When the view was read, so a recommendation can say how current it is.</param>
/// <param name="Status">The Transport Order's status; only an Accepted one can be assigned.</param>
/// <param name="RatingResult">NotYetRated, Rated, NotRatedSeparately or RatingFailed; it never blocks planning.</param>
public sealed record PlannerAgentAssignmentOptionsView(
    string TransportOrderNumber,
    string Status,
    string? Shipper,
    string? RatingResult,
    DateTimeOffset AsOf,
    PlannerAgentPlaceView? Collection,
    PlannerAgentPlaceView? Delivery,
    PlannerAgentWindowView? CollectionWindow,
    PlannerAgentWindowView? DeliveryWindow,
    PlannerAgentGoodsView Goods,
    PlannerAgentOwnFleetView OwnFleet,
    IReadOnlyList<PlannerAgentCarrierView> Carriers);

/// <param name="PostcodeArea">The first two characters of the postcode, without spaces: an area, not an address.</param>
public sealed record PlannerAgentPlaceView(string? City, string? PostcodeArea, string? Country);

public sealed record PlannerAgentWindowView(DateTimeOffset? Earliest, DateTimeOffset? Latest);

public sealed record PlannerAgentGoodsView(decimal TotalWeightKg, int PalletPlaces);

/// <param name="MeetsAssignmentRules">False when assigning the order to Own fleet breaks an Assignment rule; <paramref name="RuleBroken"/> says which.</param>
/// <param name="Fit">Fits, OverCapacity, OutsideHorizon, DayNotOpen, NoDriverWithDrivingHoursLeft or NoCollectionDay. OverCapacity and OutsideHorizon are warnings that never block the assignment.</param>
/// <param name="Warning">The warning P3 shows on assignment, in the Planner's words; null when there is none.</param>
/// <param name="Load">The collection day's load check with this order counted; null when outside the horizon or the day is not open.</param>
/// <param name="MostDrivingHoursLeft">The most Driving hours one working Driver has left on the day.</param>
public sealed record PlannerAgentOwnFleetView(
    bool MeetsAssignmentRules,
    string? RuleBroken,
    string Fit,
    DateOnly? CollectionDay,
    string? Warning,
    PlannerAgentLoadView? Load,
    int AvailableVehicles,
    int AvailableDrivers,
    int DriversWithDrivingHoursLeft,
    double? MostDrivingHoursLeft);

public sealed record PlannerAgentLoadView(decimal CapacityKg, int CapacityPalletPlaces, decimal DemandKg, int DemandPalletPlaces, bool Over);

/// <param name="CarrierCost">What the Carrier Rate Card prices the order at; null when it cannot, and <paramref name="NotPriced"/> says why (such as NoMatchingLane).</param>
public sealed record PlannerAgentCarrierView(
    string CarrierId, string Name, bool MeetsAssignmentRules, string? RuleBroken, decimal? CarrierCost, string? NotPriced);

internal static class PlannerAgentMapping
{
    public static PlannerAgentAssignmentOptionsView Of(AssignmentOptions options, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(options);

        var own = options.OwnFleet;
        return new PlannerAgentAssignmentOptionsView(
            options.TransportOrderNumber,
            options.Status.ToString(),
            options.ShipperName,
            options.RatingResult?.ToString(),
            asOf,
            Place(options.Collection),
            Place(options.Delivery),
            Window(options.CollectionEarliest, options.CollectionLatest),
            Window(options.DeliveryEarliest, options.DeliveryLatest),
            new PlannerAgentGoodsView(options.TotalWeightKg, options.PalletPlaces),
            new PlannerAgentOwnFleetView(
                own.Refusal is null,
                own.Refusal?.Code.ToString(),
                own.Fit.ToString(),
                own.Day,
                own.Warning?.Text,
                own.Load is { } load ? new PlannerAgentLoadView(load.CapacityKg, load.CapacityPalletPlaces, load.DemandKg, load.DemandPalletPlaces, load.Over) : null,
                own.AvailableVehicles,
                own.AvailableDrivers,
                own.DriversWithDrivingHoursLeft,
                own.MostDrivingHoursLeft?.TotalHours),
            [.. options.Carriers.Select(carrier => new PlannerAgentCarrierView(
                carrier.CarrierId, carrier.Name, carrier.Refusal is null, carrier.Refusal?.Code.ToString(), carrier.CarrierCost, carrier.NotPriced?.ToString()))]);
    }

    public static PlannerAgentExceptionTimelineView Of(ExceptionTimelineForAgent timeline, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var exception = timeline.Exception;
        return new PlannerAgentExceptionTimelineView(
            exception.ExceptionNumber,
            timeline.ShipmentNumber,
            exception.Type.ToString(),
            exception.Status.ToString(),
            exception.OpenedAt,
            exception.AfterDelivered,
            exception.CauseCleared,
            timeline.ShipmentStatus.ToString(),
            timeline.CarriedBy.Carrier is { } carrier ? carrier.Name ?? carrier.Id : "Own fleet",
            asOf,
            [.. exception.Causes.Select(cause => new PlannerAgentCauseView(cause.Kind.ToString(), cause.AddedAt))],
            [.. timeline.TrackingEvents.Select(entry => new PlannerAgentTrackingEventView(
                entry.Type.ToString(), entry.EventTime, entry.Place, entry.CountryCode, entry.Eta, entry.Remark, entry.Source, entry.MovedShipment))],
            [.. timeline.CarrierBookings.Select(booking => new PlannerAgentCarrierBookingView(
                booking.CarrierBookingNumber, booking.Carrier.Name ?? booking.Carrier.Id, booking.Status.ToString(), booking.RequestedAt, booking.AnsweredAt, booking.ReasonText, booking.WithdrawnAt))],
            [.. exception.Notifications.Select(notification => new PlannerAgentShipperNotificationView(
                notification.Message, notification.Status.ToString(), notification.RequestedAt, notification.SentAt))],
            [.. exception.CarrierBookingCancellations.Select(cancellation => new PlannerAgentCancellationView(
                cancellation.CarrierBookingNumber, cancellation.Reason, cancellation.CancelledAt, cancellation.CarrierNotTold))],
            [.. timeline.AgreedDeliveryWindows.Select(window => new PlannerAgentAgreedWindowView(window.Earliest, window.Latest, window.AgreedAt))],
            [.. exception.Notes.Select(note => new PlannerAgentNoteView(note.Text, note.Planner, note.AddedAt))],
            exception.Outcome?.ToString(),
            exception.ResolvedAt);
    }

    private static PlannerAgentPlaceView? Place(PlaceView? place) => place is null
        ? null
        : new PlannerAgentPlaceView(place.City, PostcodeArea(place.Postcode), place.Country);

    private static string? PostcodeArea(string? postcode)
    {
        var compact = postcode?.Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrEmpty(compact) ? null : compact[..Math.Min(2, compact.Length)].ToUpperInvariant();
    }

    private static PlannerAgentWindowView? Window(DateTimeOffset? earliest, DateTimeOffset? latest) =>
        earliest is null && latest is null ? null : new PlannerAgentWindowView(earliest, latest);
}
