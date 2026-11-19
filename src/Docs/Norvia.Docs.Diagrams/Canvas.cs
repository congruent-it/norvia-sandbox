using System.Text;

namespace Norvia.Docs.Diagrams;

/// <summary>
/// Drawing primitives shared by Norvia's Excalidraw diagram styles. A Canvas collects Excalidraw elements and an SVG preview of them.
/// The seeds are drawn from a fixed generator, so a diagram regenerates byte for byte.
/// </summary>
public sealed class Canvas
{
    public const string Ink = "#1e1e1e";
    public const string Muted = "#6b7280";
    public const string Soft = "#5c6370";
    public const string Arrow = "#495057";
    public const string ArrowPlanned = "#868e96";

    private const int Font = 6; // Excalidraw's Nunito
    private const double LineHeight = 1.25;
    private const double CharWidth = 0.57; // average glyph width / font size, used to wrap text conservatively
    private const string SvgFont = "Nunito, Helvetica, Arial, sans-serif";

    private readonly Mt19937 _random = new(7);
    private readonly List<Element> _elements = [];
    private readonly List<string> _svg = [];
    private int _count;

    public IReadOnlyList<Element> Elements => _elements;

    /// <summary>How tall a text is once wrapped, as <see cref="Wrap"/> would.</summary>
    public static Num TextHeight(string s, Num fs, Num? wrapWidth = null) => Wrap(s, fs, wrapWidth ?? 1_000_000).Count * fs * LineHeight;

    /// <summary>The width a line of text is guessed to take.</summary>
    public static double GuessWidth(string line, Num fs) => line.Length * fs.Value * CharWidth;

    /// <summary>Wraps text at a conservative guess of the glyph width. A path or an ID too long for a line breaks before a slash, then, for a long one, before a hyphen.</summary>
    public static List<string> Wrap(string s, Num fs, Num width)
    {
        var limit = width.Value;
        bool Fits(string t) => GuessWidth(t, fs) <= limit;
        var output = new List<string>();
        foreach (var paragraph in s.Split('\n'))
        {
            var line = "";
            var words = new List<string>();
            foreach (var w in paragraph.Split(' '))
            {
                if (!Fits(w) && (w.Contains('/', StringComparison.Ordinal) || (w.Contains('-', StringComparison.Ordinal) && w.Length > 28)))
                {
                    var bySlash = SplitBefore(w, "/");
                    var chunks = w.Contains('/', StringComparison.Ordinal) && bySlash.All(Fits) ? bySlash : SplitBefore(w, "/-");
                    var parts = new List<string>();
                    var current = "";
                    foreach (var chunk in chunks)
                    {
                        if (current.Length > 0 && !Fits(current + chunk))
                        {
                            parts.Add(current);
                            current = "";
                        }

                        current += chunk;
                    }

                    words.AddRange(parts);
                    words.Add(current);
                }
                else
                {
                    words.Add(w);
                }
            }

            foreach (var w in words)
            {
                var t = (line + " " + w).Trim();
                if (Fits(t) || line.Length == 0)
                {
                    line = t;
                }
                else
                {
                    output.Add(line);
                    line = w;
                }
            }

            output.Add(line);
        }

        return output;
    }

    private static List<string> SplitBefore(string w, string separators)
    {
        var chunks = new List<string>();
        var start = 0;
        for (var i = 1; i < w.Length; i++)
        {
            if (separators.Contains(w[i], StringComparison.Ordinal))
            {
                chunks.Add(w[start..i]);
                start = i;
            }
        }

        chunks.Add(w[start..]);
        return chunks;
    }

    private Element Base(string type, Num x, Num y, Num w, Num h, string? id, params (string Key, object? Value)[] extra)
    {
        _count++;
        var e = new Element
        {
            ["id"] = id ?? $"{type[..3]}{_count:D4}",
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["width"] = w,
            ["height"] = h,
            ["angle"] = (Num)0,
            ["strokeColor"] = Ink,
            ["backgroundColor"] = "transparent",
            ["fillStyle"] = "solid",
            ["strokeWidth"] = (Num)1,
            ["strokeStyle"] = "solid",
            ["roughness"] = (Num)0,
            ["opacity"] = (Num)100,
            ["groupIds"] = new List<object?>(),
            ["frameId"] = null,
            ["roundness"] = null,
            ["seed"] = _random.NextSeed(),
            ["version"] = (Num)1,
            ["versionNonce"] = _random.NextSeed(),
            ["isDeleted"] = false,
            ["boundElements"] = new List<object?>(),
            ["updated"] = 1760000000000L,
            ["link"] = null,
            ["locked"] = false,
        };
        foreach (var (key, value) in extra)
        {
            e[key] = value;
        }

        return e;
    }

    private static List<object?> Groups(IEnumerable<string>? groups) => groups is null ? [] : [.. groups];

    public Element Rect(Num x, Num y, Num w, Num h, string fill, string stroke, Num? sw = null, bool dashed = false, IEnumerable<string>? groups = null, string? id = null, Num? radius = null)
    {
        var width = sw ?? 1;
        var rx = radius ?? 10;
        var e = Base("rectangle", x, y, w, h, id,
            ("backgroundColor", fill), ("strokeColor", stroke), ("strokeWidth", width),
            ("strokeStyle", dashed ? "dashed" : "solid"), ("groupIds", Groups(groups)),
            ("roundness", rx != 0 ? new Dictionary<string, object?> { ["type"] = (Num)3 } : null));
        _elements.Add(e);
        _svg.Add($"""<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{width}"{(dashed ? " stroke-dasharray=\"7 5\"" : "")}/>""");
        return e;
    }

    /// <summary>Draws wrapped text and returns its height.</summary>
    public Num Text(Num x, Num y, Num w, string s, Num? fontSize = null, string color = Ink, string align = "left", IEnumerable<string>? groups = null, Num? wrapWidth = null, bool bold = false)
    {
        Num fs = fontSize ?? 15;
        var lines = Wrap(s, fs, wrapWidth ?? 1_000_000);
        var body = string.Join("\n", lines);
        var h = lines.Count * fs * LineHeight;
        _elements.Add(Base("text", x, y, w, h, null,
            ("strokeColor", color), ("groupIds", Groups(groups)), ("text", body), ("fontSize", fs), ("fontFamily", (Num)Font),
            ("textAlign", align), ("verticalAlign", "top"), ("baseline", (fs * 0.9).Truncate()), ("containerId", null),
            ("originalText", body), ("autoResize", false), ("lineHeight", (Num)LineHeight)));
        var anchor = align switch { "left" => "start", "center" => "middle", _ => "end" };
        var tx = align == "left" ? x : (align == "center" ? x + (w / 2) : x + w);
        for (var i = 0; i < lines.Count; i++)
        {
            _svg.Add($"""<text x="{tx}" y="{y + (i * fs * LineHeight) + (fs * 0.92)}" font-size="{fs}" fill="{color}" text-anchor="{anchor}" font-family="{SvgFont}"{(bold ? " font-weight=\"700\"" : "")}>{Escape(lines[i])}</text>""");
        }

        return h;
    }

    public Element Ellipse(Num x, Num y, Num w, Num h, string fill, string stroke, Num? sw = null, bool dashed = false)
    {
        var width = sw ?? 2;
        var e = Base("ellipse", x, y, w, h, null,
            ("backgroundColor", fill), ("strokeColor", stroke), ("strokeWidth", width),
            ("strokeStyle", dashed ? "dashed" : "solid"), ("roundness", new Dictionary<string, object?> { ["type"] = (Num)2 }));
        _elements.Add(e);
        _svg.Add($"""<ellipse cx="{x + (w / 2)}" cy="{y + (h / 2)}" rx="{w / 2}" ry="{h / 2}" fill="{fill}" stroke="{stroke}" stroke-width="{width}"{(dashed ? " stroke-dasharray=\"7 5\"" : "")}/>""");
        return e;
    }

    public Element Diamond(Num x, Num y, Num w, Num h, string fill, string stroke, Num? sw = null)
    {
        var width = sw ?? 2;
        var e = Base("diamond", x, y, w, h, null,
            ("backgroundColor", fill), ("strokeColor", stroke), ("strokeWidth", width), ("roundness", new Dictionary<string, object?> { ["type"] = (Num)2 }));
        _elements.Add(e);
        var points = $"{x + (w / 2)},{y} {x + w},{y + (h / 2)} {x + (w / 2)},{y + h} {x},{y + (h / 2)}";
        _svg.Add($"""<polygon points="{points}" fill="{fill}" stroke="{stroke}" stroke-width="{width}" stroke-linejoin="round"/>""");
        return e;
    }

    /// <summary>Text read bottom to top, centred on (cx, cy), at most <paramref name="length"/> long: a lane's label.</summary>
    public void VerticalText(Num cx, Num cy, Num length, string s, Num? fontSize = null, string color = Ink, bool bold = false)
    {
        Num fs = fontSize ?? 13;
        var lines = Wrap(s, fs, length);
        var body = string.Join("\n", lines);
        var h = lines.Count * fs * LineHeight;
        _elements.Add(Base("text", cx - (length / 2), cy - (h / 2), length, h, null,
            ("strokeColor", color), ("text", body), ("fontSize", fs), ("fontFamily", (Num)Font), ("textAlign", "center"),
            ("verticalAlign", "top"), ("baseline", (fs * 0.9).Truncate()), ("containerId", null), ("originalText", body),
            ("autoResize", false), ("lineHeight", (Num)LineHeight), ("angle", (Num)(-1.5707963267948966))));
        for (var i = 0; i < lines.Count; i++)
        {
            var ty = cy - (h / 2) + (i * fs * LineHeight) + (fs * 0.92);
            _svg.Add($"""<text x="{cx}" y="{ty}" font-size="{fs}" fill="{color}" text-anchor="middle" transform="rotate(-90 {cx} {cy})" font-family="{SvgFont}"{(bold ? " font-weight=\"700\"" : "")}>{Escape(lines[i])}</text>""");
        }
    }

    public Element DrawArrow(IReadOnlyList<Pt> points, string color, bool dashed, bool both, string? start, string? end, bool plain = false)
    {
        var origin = points[0];
        var relative = points.Select(p => new List<object?> { p.X - origin.X, p.Y - origin.Y }).ToList();
        var w = Num.Max(points.Select(p => p.X - origin.X)) - Num.Min(points.Select(p => p.X - origin.X));
        var h = Num.Max(points.Select(p => p.Y - origin.Y)) - Num.Min(points.Select(p => p.Y - origin.Y));
        var e = Base("arrow", origin.X, origin.Y, w, h, null,
            ("strokeColor", color), ("strokeWidth", (Num)2), ("strokeStyle", dashed ? "dashed" : "solid"),
            ("points", relative), ("lastCommittedPoint", null),
            ("startBinding", start is { Length: > 0 } ? Binding(start) : null),
            ("endBinding", end is { Length: > 0 } ? Binding(end) : null),
            ("startArrowhead", both ? "arrow" : null), ("endArrowhead", plain ? null : "arrow"), ("elbowed", false));
        _elements.Add(e);
        var path = "M " + string.Join(" L ", points.Select(p => $"{p.X} {p.Y}"));
        _svg.Add($"""<path d="{path}" fill="none" stroke="{color}" stroke-width="2" stroke-linejoin="round"{(plain ? "" : " marker-end=\"url(#ah)\"")}{(both ? " marker-start=\"url(#ah)\"" : "")}{(dashed ? " stroke-dasharray=\"7 5\"" : "")}/>""");
        return e;
    }

    private static Dictionary<string, object?> Binding(string id) => new() { ["elementId"] = id, ["focus"] = (Num)0, ["gap"] = (Num)4 };

    /// <summary>Writes the <c>.excalidraw</c> file and, if asked, the SVG preview.</summary>
    public void Save(Num width, Num height, string excalidrawPath, string? svgPath = null)
    {
        var document = new Dictionary<string, object?>
        {
            ["type"] = "excalidraw",
            ["version"] = (Num)2,
            ["source"] = "https://excalidraw.com",
            ["elements"] = _elements,
            ["appState"] = new Dictionary<string, object?> { ["gridSize"] = null, ["viewBackgroundColor"] = "#ffffff" },
            ["files"] = new Dictionary<string, object?>(),
        };
        File.WriteAllText(excalidrawPath, ExcalidrawJson.Write(document));
        if (svgPath is null)
        {
            return;
        }

        var head = $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}"><defs><marker id="ah" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M 0 1 L 9 5 L 0 9" fill="none" stroke="context-stroke" stroke-width="1.6"/></marker></defs><rect width="{width}" height="{height}" fill="#ffffff"/>""";
        File.WriteAllText(svgPath, head + string.Concat(_svg) + "</svg>");
    }

    private static string Escape(string s) => new StringBuilder(s).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").ToString();
}
