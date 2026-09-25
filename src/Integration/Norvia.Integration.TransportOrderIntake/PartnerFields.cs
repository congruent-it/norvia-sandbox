using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Reads the fields of a partner booking, or of one of its items, leniently: a value of the wrong kind reads as absent.</summary>
internal sealed class PartnerFields(JsonNode? node)
{
    public string? Text(string name) =>
        Value(name) is { } value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    public int? Integer(string name) =>
        Value(name) is { } value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>Every item keeps its place, even one that isn't an object, so the TMS numbers the goods lines as the Shipper did.</summary>
    public IEnumerable<PartnerFields>? Items() =>
        (node as JsonObject)?["items"] is JsonArray items ? items.Select(item => new PartnerFields(item)) : null;

    private JsonValue? Value(string name) => (node as JsonObject)?[name] as JsonValue;
}
