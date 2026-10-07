using System.Globalization;
using System.Text.Json;
using DotLiquid;
using DotLiquid.NamingConventions;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Liquid maps API Management's <c>set-body</c> runs on the TMS's Canonical Model answer, infra/estate/partner-api/
/// shipper-tracking.liquid and shipper-tracking-list.liquid, run here with the same DotLiquid version in the same C# mode
/// (ADR 0035). The body reaches a template as dictionaries and lists, with its date strings left as text, as the policy's
/// <c>parse-date="false"</c> has it.
/// </summary>
internal sealed class ShipperTrackingMap
{
    private readonly Template _detail = Parse("shipper-tracking.liquid");
    private readonly Template _list = Parse("shipper-tracking-list.liquid");

    // A parsed DotLiquid template keeps its state while it renders, so the calls take turns.
    private readonly Lock _render = new();

    /// <summary>One Transport Order's Canonical Model detail, as the Shipper tracking format.</summary>
    public string Detail(string canonicalDetail) => Run(_detail, canonicalDetail);

    /// <summary>A Canonical Model list of Transport Orders, as the Shipper tracking list format.</summary>
    public string List(string canonicalList) => Run(_list, canonicalList);

    private static Template Parse(string name)
    {
        // API Management's Liquid runs in C# mode: Pascal case filters and members. It is a process-wide setting of DotLiquid.
        Template.NamingConvention = new CSharpNamingConvention();
        using var stream = typeof(ShipperTrackingMap).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not an embedded map.");
        using var reader = new StreamReader(stream);
        return Template.Parse(reader.ReadToEnd());
    }

    private string Run(Template template, string canonical)
    {
        var body = ToLiquid(JsonDocument.Parse(canonical).RootElement);
        lock (_render)
        {
            return template.Render(Hash.FromDictionary(new Dictionary<string, object?> { ["body"] = body }), CultureInfo.InvariantCulture);
        }
    }

    private static object? ToLiquid(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ToLiquid(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToLiquid).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
