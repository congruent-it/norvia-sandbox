using System.Globalization;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>How a Message trail's keys and outcome read on the page.</summary>
public static class TrailText
{
    public const string NoOutcome = "No outcome recorded";

    public static string Of(Outcome? outcome) => outcome?.ToString() ?? NoOutcome;

    public static string Of(BusinessKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"{KindOf(key.Kind)} {key.Value}";
    }

    private static string KindOf(BusinessKeyKind kind) => kind switch
    {
        BusinessKeyKind.TransportOrderNumber => "Transport Order",
        BusinessKeyKind.ShipmentNumber => "Shipment",
        BusinessKeyKind.CarrierBookingNumber => "Carrier booking",
        BusinessKeyKind.TripNumber => "Trip",
        BusinessKeyKind.SourceMessageId => "Source message",
        _ => kind.ToString(),
    };

    /// <summary>"250 ms", "1.5 s", "2 min 5 s", or a dash when the telemetry recorded only a moment.</summary>
    public static string Of(TimeSpan? duration) => duration switch
    {
        null => "–",
        { TotalSeconds: < 1 } milliseconds => string.Create(CultureInfo.InvariantCulture, $"{milliseconds.TotalMilliseconds:0} ms"),
        { TotalMinutes: < 1 } seconds => string.Create(CultureInfo.InvariantCulture, $"{seconds.TotalSeconds:0.#} s"),
        { } minutes => string.Create(CultureInfo.InvariantCulture, $"{(int)minutes.TotalMinutes} min {minutes.Seconds} s"),
    };

    /// <summary>"1 Message trail", "2 Message trails".</summary>
    public static string Count(int trails) => trails == 1 ? "1 Message trail" : $"{trails} Message trails";
}
