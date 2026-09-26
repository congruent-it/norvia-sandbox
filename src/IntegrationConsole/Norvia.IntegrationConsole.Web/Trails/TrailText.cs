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

    /// <summary>"1 Message trail", "2 Message trails".</summary>
    public static string Count(int trails) => trails == 1 ? "1 Message trail" : $"{trails} Message trails";
}
