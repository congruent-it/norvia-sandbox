using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Application.Reports;

/// <summary>The one place the kinds of report in the Domain, the actions the Driver takes and the Tracking event types of the TMS are mapped to each other.</summary>
internal static class ReportKindMapping
{
    public static ReportKind ToDomain(this StopActionDto action) => action switch
    {
        StopActionDto.Collected => ReportKind.Collected,
        StopActionDto.CollectionFailed => ReportKind.CollectionFailed,
        StopActionDto.DeliveryFailed => ReportKind.DeliveryFailed,
        StopActionDto.Damaged => ReportKind.Damaged,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "An action the Driver app has no report for."),
    };

    public static ReportKindDto ToDto(this ReportKind kind) => kind switch
    {
        ReportKind.StartTrip => ReportKindDto.StartTrip,
        ReportKind.Collected => ReportKindDto.Collected,
        ReportKind.CollectionFailed => ReportKindDto.CollectionFailed,
        ReportKind.DeliveryFailed => ReportKindDto.DeliveryFailed,
        ReportKind.Damaged => ReportKindDto.Damaged,
        ReportKind.Delivered => ReportKindDto.Delivered,
        ReportKind.ProofOfDelivery => ReportKindDto.ProofOfDelivery,
        ReportKind.EndTrip => ReportKindDto.EndTrip,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A kind of report with no view."),
    };

    /// <summary>The Tracking event (P5) a Stop report records: the Canonical Model names them as the kinds of report are named.</summary>
    public static string ToTrackingEventType(this ReportKind kind) => kind.ToString();
}
