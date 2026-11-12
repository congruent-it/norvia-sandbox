using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>
/// Builds the input of a run (ADR 0049): three delimited parts, the Shipper's question with the last turns, the tracking data and the current
/// date and time. The question and every string in the data are data, so the delimiters can't be closed from inside them.
/// </summary>
public static class AssistantInputBuilder
{
    /// <summary>How many earlier turns go with a question.</summary>
    public const int MaxTurns = 4;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AssistantInput Build(string question, IReadOnlyList<AssistantTurn> turns, TrackingData data, DateTimeOffset now, AssistantLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(data);

        limits ??= new AssistantLimits();
        var capped = new TrackingData(
            data.Detail is null ? null : Cap(data.Detail, limits),
            [.. data.Orders.Take(limits.MaxOrders)],
            data.Found is null ? null : Cap(data.Found, limits));
        var node = JsonSerializer.SerializeToNode(capped, Json)!;
        // Angle brackets only occur inside strings, where the escape is the same text for a reader.
        var dataText = node.ToJsonString(Json).Replace("<", "\\u003C", StringComparison.Ordinal).Replace(">", "\\u003E", StringComparison.Ordinal);
        var nowText = now.UtcDateTime.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";

        var text = new System.Text.StringBuilder();
        text.AppendLine("<question>");
        foreach (var turn in turns.TakeLast(MaxTurns))
        {
            text.Append("Earlier question: ").AppendLine(Defang(turn.Question));
            text.Append("Earlier answer: ").AppendLine(Defang(turn.Answer));
        }

        text.AppendLine(Defang(question)).AppendLine("</question>");
        text.AppendLine("<tracking_data>").AppendLine(dataText).AppendLine("</tracking_data>");
        text.AppendLine("<now>").AppendLine(nowText).AppendLine("</now>");
        return new AssistantInput(text.ToString(), node, dataText, nowText);
    }

    private static ShipperTracking.ShipperTrackingDetail Cap(ShipperTracking.ShipperTrackingDetail detail, AssistantLimits limits) =>
        detail with { Progress = [.. detail.Progress.Take(limits.MaxEventsPerOrder)] };

    // Angle brackets can't open or close a delimiter from inside the question or an earlier turn.
    private static string Defang(string text) => text.Replace("<", "‹", StringComparison.Ordinal).Replace(">", "›", StringComparison.Ordinal);
}
