namespace Norvia.Docs.Diagrams;

/// <summary>A palette entry: the lane label colour, the lane tint, the box fill and the box stroke.</summary>
public readonly record struct Tone(string Label, string Tint, string Fill, string Stroke);

/// <summary>A legend entry: a swatch and what it says.</summary>
public readonly record struct LegendItem(string Label, string Fill, string Stroke, bool Dashed = false);

/// <summary>The style tokens every style draws with: one colour per application, in every diagram.</summary>
public static class Palette
{
    public static readonly IReadOnlyDictionary<string, Tone> Tones = new Dictionary<string, Tone>
    {
        ["gray"] = new("#6b7280", "#f6f7f8", "#e9ecef", "#868e96"),      // outside Norvia
        ["violet"] = new("#7048e8", "#f6f2ff", "#e5dbff", "#7048e8"),    // integration layer
        ["blue"] = new("#1c7ed6", "#eff7ff", "#d0ebff", "#1c7ed6"),      // TMS
        ["teal"] = new("#0c8599", "#effafc", "#c5f6fa", "#0c8599"),      // Driver app
        ["green"] = new("#2f9e44", "#f1fbf3", "#d3f9d8", "#2f9e44"),     // ERP
        ["pink"] = new("#d6336c", "#fff0f6", "#ffdeeb", "#d6336c"),      // Integration console
        ["orange"] = new("#e8590c", "#fff4e6", "#ffe8cc", "#e8590c"),    // Simulator
        ["lightgray"] = new("#adb5bd", "#f8f9fa", "#f1f3f5", "#adb5bd"), // planned (use with dashed)
    };

    public static readonly IReadOnlyDictionary<string, string> StreamTint = new Dictionary<string, string>
    {
        ["blue"] = "#e7f5ff",
        ["green"] = "#ebfbee",
        ["orange"] = "#fff4e6",
        ["violet"] = "#f3f0ff",
        ["pink"] = "#fff0f6",
    };

    /// <summary>A person acts: fill, stroke.</summary>
    public static readonly (string Fill, string Stroke) Person = ("#fff3bf", "#f59f00");

    /// <summary>Planned, or reference data: fill, stroke. Always dashed.</summary>
    public static readonly (string Fill, string Stroke) Planned = ("#f1f3f5", "#adb5bd");

    public static readonly IReadOnlyList<LegendItem> DefaultLegend =
    [
        new("A person acts", Person.Fill, Person.Stroke),
        new("The application decides", "#d0ebff", "#1c7ed6"),
        new("An integration flow runs", "#e5dbff", "#7048e8"),
        new("Planned, or reference data", Planned.Fill, Planned.Stroke, true),
    ];

    public static Tone Of(string key) => Tones[key];

    /// <summary>A legend entry in an application's colours.</summary>
    public static LegendItem Legend(string label, string key) => new(label, Tones[key].Fill, Tones[key].Stroke);

    public static LegendItem LegendPerson(string label = "A person") => new(label, Person.Fill, Person.Stroke);

    public static LegendItem LegendPlanned(string label = "Planned, not built yet") => new(label, Planned.Fill, Planned.Stroke, true);

    /// <summary>The fill, stroke and dash of a box of this kind in a lane of this tone.</summary>
    public static (string Fill, string Stroke, bool Dashed) ForKind(string? kind, Tone tone) => kind switch
    {
        "person" => (Person.Fill, Person.Stroke, false),
        "planned" => (Planned.Fill, Planned.Stroke, true),
        _ => (tone.Fill, tone.Stroke, false),
    };
}
