#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// Draws the BPMN models of docs/processes in Norvia's Excalidraw style.
//
//     dotnet run docs/architecture/diagrams/bpmn.cs -- [process ...]    # e.g. p1-transport-order-intake; writes bpmn-<process>.excalidraw and .png here
//
// The .bpmn files stay the source: they open in any BPMN tool and carry their own layout (the bpmndi section), which is kept. This only
// restyles what they say: a person's task is yellow, a system's takes its lane's colour, events are rings, gateways diamonds. Needs rsvg-convert for the PNGs.

using System.Globalization;
using System.Xml.Linq;
using Norvia.Docs.Diagrams;

const int M = 40;
const int Top = 120;
const int Strip = 30; // width of a lane's or pool's label strip

XNamespace model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
XNamespace di = "http://www.omg.org/spec/BPMN/20100524/DI";
XNamespace dc = "http://www.omg.org/spec/DD/20100524/DC";
XNamespace ddi = "http://www.omg.org/spec/DD/20100524/DI";

var here = DiagramOutput.SpecDirectory();
var sources = new[] { Path.Combine(here, "..", "..", "processes", "tms"), Path.Combine(here, "..", "..", "processes", "erp") };
var files = sources.SelectMany(folder => Directory.GetFiles(folder, "*.bpmn")).Order(StringComparer.Ordinal).ToList();
foreach (var path in files)
{
    var stem = Path.GetFileNameWithoutExtension(path);
    if (args.Length > 0 && !args.Contains(stem))
    {
        continue;
    }

    var output = Path.Combine(here, $"bpmn-{stem}.excalidraw");
    var svg = Path.Combine(Path.GetTempPath(), $"bpmn-{stem}.svg");
    Render(path, output, svg);
    DiagramOutput.ToPng(svg, Path.ChangeExtension(output, ".png"), Console.Out);
    Console.WriteLine($"wrote {Path.GetFileName(output)}");
}

static string PaletteOf(string? name)
{
    var n = (name ?? "").ToLowerInvariant();
    (string[] Words, string Key)[] rules =
    [
        (["driver app", "driver"], "teal"),
        (["erp", "finance"], "green"),
        (["api management", "logic app", "service bus", "queue", "topic"], "violet"),
        (["tms", "planner", "customer service"], "blue"),
        (["portal", "shipper", "carrier", "persona", "partner"], "gray"),
    ];
    foreach (var (words, key) in rules)
    {
        if (words.Any(w => n.Contains(w, StringComparison.Ordinal)))
        {
            return key;
        }
    }

    return "blue";
}

static Num Parse(string? text) => double.Parse(text!, CultureInfo.InvariantCulture);

void Render(string path, string excalidrawPath, string svgPath)
{
    var root = XDocument.Load(path).Root!;
    Dictionary<string, XElement> elements = [];
    foreach (var el in root.DescendantsAndSelf())
    {
        if (el.Attribute("id") is { Value.Length: > 0 } id && el.Name.Namespace == model)
        {
            elements[id.Value] = el;
        }
    }

    Dictionary<string, string> laneOf = [];
    Dictionary<string, string> lanes = [];
    Dictionary<string, string> pools = [];
    foreach (var lane in root.Descendants(model + "lane"))
    {
        lanes[lane.Attribute("id")!.Value] = lane.Attribute("name")?.Value ?? "";
        foreach (var reference in lane.Elements(model + "flowNodeRef"))
        {
            laneOf[reference.Value] = lane.Attribute("name")?.Value ?? "";
        }
    }

    foreach (var p in root.Descendants(model + "participant"))
    {
        pools[p.Attribute("id")!.Value] = p.Attribute("name")?.Value ?? "";
    }

    var title = pools.Count > 0 ? pools.Values.First() : Path.GetFileNameWithoutExtension(path);

    Dictionary<string, (Num X, Num Y, Num W, Num H, bool Marker)> shapes = [];
    Dictionary<string, (Num X, Num Y, Num W, Num H)> labels = [];
    Dictionary<string, (List<Pt> Points, (Num X, Num Y, Num W, Num H)? Label)> edges = [];
    (Num, Num, Num, Num) BoundsOf(XElement bounds) => (Parse(bounds.Attribute("x")?.Value), Parse(bounds.Attribute("y")?.Value), Parse(bounds.Attribute("width")?.Value), Parse(bounds.Attribute("height")?.Value));
    foreach (var shape in root.Descendants(di + "BPMNShape"))
    {
        var b = BoundsOf(shape.Element(dc + "Bounds")!);
        var key = shape.Attribute("bpmnElement")!.Value;
        shapes[key] = (b.Item1, b.Item2, b.Item3, b.Item4, shape.Attribute("isMarkerVisible")?.Value == "true");
        if (shape.Element(di + "BPMNLabel")?.Element(dc + "Bounds") is { } lb)
        {
            labels[key] = BoundsOf(lb);
        }
    }

    foreach (var edge in root.Descendants(di + "BPMNEdge"))
    {
        var points = edge.Elements(ddi + "waypoint").Select(w => new Pt(Parse(w.Attribute("x")?.Value), Parse(w.Attribute("y")?.Value))).ToList();
        (Num, Num, Num, Num)? label = edge.Element(di + "BPMNLabel")?.Element(dc + "Bounds") is { } eb ? BoundsOf(eb) : null;
        edges[edge.Attribute("bpmnElement")!.Value] = (points, label);
    }

    var allPoints = edges.Values.SelectMany(e => e.Points).ToList();
    var minX = Num.Min(shapes.Values.Select(s => s.X).Concat(allPoints.Select(p => p.X)));
    var minY = Num.Min(shapes.Values.Select(s => s.Y).Concat(allPoints.Select(p => p.Y)));
    var maxX = Num.Max(shapes.Values.Select(s => s.X + s.W).Concat(allPoints.Select(p => p.X)));
    var maxY = Num.Max(shapes.Values.Select(s => s.Y + s.H).Concat(allPoints.Select(p => p.Y)));
    Num dx = M - minX, dy = Top - minY;
    var width = maxX - minX + (2 * M);
    var height = maxY - minY + Top + 70;
    Pt T(Num x, Num y) => new(x + dx, y + dy);

    var cv = new Canvas();
    cv.Text(M, 30, width - (2 * M), $"{title} · BPMN", 34, Canvas.Ink, bold: true);
    cv.Text(M, 74, width - (2 * M), "The process as modelled in the .bpmn file beside its page, in Norvia's colours.", 15, Canvas.Muted);
    LegendItem[] legend = [Palette.LegendPerson("A person acts"), new("A system acts", Palette.Of("blue").Fill, Palette.Of("blue").Stroke)];
    Num lx = M;
    foreach (var item in legend)
    {
        cv.Rect(lx, 98, 26, 18, item.Fill, item.Stroke, 2, item.Dashed);
        cv.Text(lx + 34, 99, (item.Label.Length * 8.6) + 6, item.Label, 14, Canvas.Ink);
        lx += 34 + (item.Label.Length * 8.6) + 40;
    }

    // pools and lanes, the biggest first
    var frames = pools.Where(p => shapes.ContainsKey(p.Key)).Select(p => (Id: p.Key, Name: p.Value, Kind: "pool"))
        .Concat(lanes.Where(l => shapes.ContainsKey(l.Key)).Select(l => (Id: l.Key, Name: l.Value, Kind: "lane")))
        .OrderBy(f => -(shapes[f.Id].W.Value * shapes[f.Id].H.Value))
        .ToList();
    foreach (var (id, name, kind) in frames)
    {
        var s = shapes[id];
        var p = T(s.X, s.Y);
        var tone = Palette.Of(PaletteOf(name));
        cv.Rect(p.X, p.Y, s.W, s.H, kind == "pool" ? "#ffffff" : tone.Tint, kind == "lane" ? tone.Stroke : "#adb5bd", 1.5, radius: 4);
        if (kind == "lane" || elements[id].Attribute("processRef") is not null)
        {
            cv.Rect(p.X, p.Y, Strip, s.H, tone.Label, tone.Label, 1, radius: 4);
            cv.VerticalText(p.X + (Strip / 2), p.Y + (s.H / 2), s.H - 12, name, 12, "#ffffff", bold: true);
        }
        else
        {
            cv.Rect(p.X, p.Y, s.W, s.H, "#ffffff", "#adb5bd", 1.5, dashed: true, radius: 4);
            cv.Text(p.X + 12, p.Y + (s.H / 2) - 8, s.W - 24, name, 13, Canvas.Muted, "center", wrapWidth: s.W - 24, bold: true);
        }
    }

    // sequence and message flows
    foreach (var (fid, (points, lb)) in edges)
    {
        if (!elements.TryGetValue(fid, out var el))
        {
            continue;
        }

        var message = el.Name.LocalName == "messageFlow";
        cv.DrawArrow([.. points.Select(p => T(p.X, p.Y))], message ? Canvas.ArrowPlanned : Canvas.Arrow, message, false, null, null);
        var label = el.Attribute("name")?.Value;
        if (!string.IsNullOrEmpty(label) && lb is { } box)
        {
            var p = T(box.X, box.Y);
            cv.Rect(p.X - 2, p.Y - 1, box.W + 4, box.H + 2, "#ffffff", "#ffffff", 0, radius: 3);
            cv.Text(p.X, p.Y, box.W, label, 11, Canvas.Ink, "center", wrapWidth: box.W + 8);
        }
    }

    // tasks, gateways and events
    string[] taskKinds = ["serviceTask", "userTask", "businessRuleTask", "callActivity", "task", "sendTask", "receiveTask", "scriptTask", "manualTask"];
    foreach (var (i, s) in shapes)
    {
        if (!elements.TryGetValue(i, out var el))
        {
            continue;
        }

        var kind = el.Name.LocalName;
        var pos = T(s.X, s.Y);
        Num x = pos.X, y = pos.Y, w = s.W, h = s.H;
        var name = el.Attribute("name")?.Value ?? "";
        var lanePal = Palette.Of(PaletteOf(laneOf.GetValueOrDefault(i, "")));
        if (taskKinds.Contains(kind))
        {
            var person = kind is "userTask" or "manualTask";
            var (fill, stroke) = person ? Palette.Person : (lanePal.Fill, lanePal.Stroke);
            cv.Rect(x, y, w, h, fill, stroke, kind == "callActivity" ? 3 : 2);
            Num fs = 12;
            while (fs > 9 && Canvas.TextHeight(name, fs, w - 14) > h - 8)
            {
                // a long label shrinks to fit its task
                fs -= 1;
            }

            var th = Canvas.TextHeight(name, fs, w - 14);
            cv.Text(x + 6, y + ((h - th) / 2), w - 12, name, fs, Canvas.Ink, "center", wrapWidth: w - 14);
        }
        else if (kind.EndsWith("Gateway", StringComparison.Ordinal))
        {
            cv.Diamond(x, y, w, h, "#ffffff", "#495057", 2);
            Num cx = x + (w / 2), cy = y + (h / 2), r = Num.Min(w, h) * 0.2;
            if (kind == "parallelGateway")
            {
                cv.DrawArrow([new Pt(cx - r, cy), new Pt(cx + r, cy)], "#495057", false, false, null, null, plain: true);
                cv.DrawArrow([new Pt(cx, cy - r), new Pt(cx, cy + r)], "#495057", false, false, null, null, plain: true);
            }
            else if (s.Marker || kind == "exclusiveGateway")
            {
                cv.DrawArrow([new Pt(cx - r, cy - r), new Pt(cx + r, cy + r)], "#495057", false, false, null, null, plain: true);
                cv.DrawArrow([new Pt(cx - r, cy + r), new Pt(cx + r, cy - r)], "#495057", false, false, null, null, plain: true);
            }
        }
        else if (kind.EndsWith("Event", StringComparison.Ordinal))
        {
            var (ring, sw) = kind switch
            {
                "startEvent" => ("#2f9e44", 2),
                "endEvent" => ("#495057", 4),
                _ => ("#1c7ed6", 2),
            };
            cv.Ellipse(x, y, w, h, "#ffffff", ring, sw, dashed: el.Attribute("cancelActivity")?.Value == "false");
            if (kind is "intermediateThrowEvent" or "intermediateCatchEvent" or "boundaryEvent")
            {
                cv.Ellipse(x + 4, y + 4, w - 8, h - 8, "#ffffff", ring, 1);
            }

            Num cx = x + (w / 2), cy = y + (h / 2);
            foreach (var definition in el.Elements())
            {
                switch (definition.Name.LocalName)
                {
                    case "messageEventDefinition":
                        var solid = kind is "endEvent" or "intermediateThrowEvent";
                        cv.Rect(cx - 7, cy - 5, 14, 10, solid ? "#495057" : "#ffffff", "#495057", 1.5, radius: 0);
                        cv.DrawArrow([new Pt(cx - 7, cy - 5), new Pt(cx, cy + 1), new Pt(cx + 7, cy - 5)], solid ? "#ffffff" : "#495057", false, false, null, null, plain: true);
                        break;
                    case "errorEventDefinition":
                        cv.Text(cx - 6, cy - 9, 12, "!", 16, "#c92a2a", "center", bold: true);
                        break;
                    case "timerEventDefinition":
                        cv.Ellipse(cx - 7, cy - 7, 14, 14, "#ffffff", "#495057", 1.5);
                        cv.DrawArrow([new Pt(cx, cy - 4), new Pt(cx, cy), new Pt(cx + 3, cy + 2)], "#495057", false, false, null, null, plain: true);
                        break;
                    case "conditionalEventDefinition":
                        cv.Text(cx - 6, cy - 9, 12, "?", 16, "#495057", "center", bold: true);
                        break;
                }
            }
        }
        else if (kind == "textAnnotation")
        {
            cv.Rect(x, y, w, h, "#fff9db", "#f59f00", 1, radius: 4);
            var body = (el.Element(model + "text")?.Value ?? "").Trim();
            cv.Text(x + 6, y + 5, w - 12, body, 11, Canvas.Ink, wrapWidth: w - 14);
        }

        (Num X, Num Y, Num W, Num H)? lb = labels.TryGetValue(i, out var known) ? known : null;
        var isEventOrGateway = kind.EndsWith("Event", StringComparison.Ordinal) || kind.EndsWith("Gateway", StringComparison.Ordinal);
        if (name.Length > 0 && lb is null && kind == "boundaryEvent" && el.Attribute("attachedToRef")?.Value is { } attached && shapes.TryGetValue(attached, out var host))
        {
            // no position in the file: on the outside of the task edge the event sits on, never over the task's own text
            Num ex = x - dx, ey = y - dy;
            Num bx = ex + (w / 2), by = ey + (h / 2);
            var side = new[]
            {
                (Distance: Num.Abs(by - host.Y), Side: "top"),
                (Distance: Num.Abs(by - (host.Y + host.H)), Side: "bottom"),
                (Distance: Num.Abs(bx - host.X), Side: "left"),
                (Distance: Num.Abs(bx - (host.X + host.W)), Side: "right"),
            }.Aggregate((best, next) => next.Distance < best.Distance || (next.Distance == best.Distance && string.CompareOrdinal(next.Side, best.Side) < 0) ? next : best).Side;
            lb = side switch
            {
                "top" => (bx - 45, ey - 30, 90, 28),
                "bottom" => (bx - 45, ey + h + 3, 90, 28),
                "left" => (ex - 96, by - 14, 90, 28),
                _ => (ex + w + 6, by - 14, 90, 28),
            };
        }
        else if (name.Length > 0 && lb is null && isEventOrGateway)
        {
            // no position in the file: under the shape
            lb = (x - dx - 30, y - dy + h + 3, w + 60, 30);
        }

        if (lb is { } at && name.Length > 0 && isEventOrGateway)
        {
            var label = T(at.X, at.Y);
            cv.Text(label.X, label.Y, at.W, name, 11, Canvas.Ink, "center", wrapWidth: at.W + 8);
        }
    }

    cv.Save(width, height, excalidrawPath, svgPath);
}
