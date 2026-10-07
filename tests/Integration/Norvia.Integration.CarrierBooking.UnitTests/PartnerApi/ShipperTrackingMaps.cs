using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotLiquid;
using DotLiquid.NamingConventions;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management's Liquid maps from the TMS's Canonical Model answer to the Shipper tracking format,
/// infra/estate/partner-api/shipper-tracking.liquid and shipper-tracking-list.liquid, run with the DotLiquid version
/// API Management's set-body policy uses, in the C# mode it configures. The body reaches the template as dictionaries
/// and lists, with its date strings left as text (the policy's <c>parse-date="false"</c>).
/// </summary>
internal static class ShipperTrackingMaps
{
    private static readonly Lazy<Template> DetailTemplate = new(() => Parse("shipper-tracking.liquid"));
    private static readonly Lazy<Template> ListTemplate = new(() => Parse("shipper-tracking-list.liquid"));

    // A parsed DotLiquid template keeps its state while it renders, so the tests, which run in parallel, take turns.
    private static readonly Lock RenderLock = new();

    /// <summary>Maps one Transport Order's Canonical Model detail.</summary>
    public static JsonNode Detail(string canonicalDetail) => Run(DetailTemplate.Value, canonicalDetail);

    /// <summary>Maps a Canonical Model list of Transport Orders.</summary>
    public static JsonNode List(string canonicalList) => Run(ListTemplate.Value, canonicalList);

    private static Template Parse(string name)
    {
        // API Management's Liquid runs in C# mode: Pascal case filters and members. It is a process-wide setting of DotLiquid.
        Template.NamingConvention = new CSharpNamingConvention();
        return Template.Parse(Contracts.Read($"PartnerApiMaps/{name}"));
    }

    private static JsonNode Run(Template template, string canonical)
    {
        var body = ToLiquid(JsonDocument.Parse(canonical).RootElement);
        string text;
        lock (RenderLock)
        {
            text = template.Render(Hash.FromDictionary(new Dictionary<string, object?> { ["body"] = body }), CultureInfo.InvariantCulture);
        }

        return JsonNode.Parse(text) ?? throw new InvalidOperationException("The map gave no JSON.");
    }

    private static object? ToLiquid(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ToLiquid(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToLiquid).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetInt64(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
