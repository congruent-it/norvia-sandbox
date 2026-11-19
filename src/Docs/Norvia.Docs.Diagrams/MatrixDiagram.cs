namespace Norvia.Docs.Diagrams;

/// <summary>A lane: an application, one row of the matrix.</summary>
public sealed record MatrixLane(string Key, string Palette, string Name, string Sub, bool Dashed = false);

/// <summary>A column header and the process IDs under it.</summary>
public sealed record MatrixColumn(string Name, string Sub);

/// <summary>A value stream: a coloured band above some columns.</summary>
public sealed record MatrixBand(string Name, string Palette, IReadOnlyList<MatrixColumn> Cols);

/// <summary>A full-width band under the lanes, for an actor that cuts across.</summary>
public sealed record MatrixCross(string Name, string Sub, string Palette, string Headline, string Detail);

/// <summary>A box in a column and a lane; <c>Slot</c> stacks boxes in a lane from 0, <c>Kind</c> is person, auto or planned.</summary>
public sealed record MatrixStep(string Id, int Column, string Lane, int Slot, string Kind, string Title, string Sub);

/// <summary>An arrow between two boxes: <c>v</c>, <c>h</c>, <c>gr</c> or <c>gl</c>.</summary>
public sealed record MatrixEdge(string From, string To, string Route, bool Dashed = false, bool Both = false, Num? Off = null, Num? Sdy = null, Num? Ddy = null);

/// <summary>A small label by a box, such as on a branch.</summary>
public sealed record MatrixLabel(string Text, string Step, Num Dx, Num Dy);

/// <summary>The matrix style's spec; see README.md.</summary>
public sealed record MatrixSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Footnote { get; init; }

    public required IReadOnlyList<MatrixLane> Lanes { get; init; }

    public required IReadOnlyList<MatrixBand> Bands { get; init; }

    public IReadOnlyList<MatrixCross> Cross { get; init; } = [];

    public required IReadOnlyList<MatrixStep> Steps { get; init; }

    public IReadOnlyList<MatrixEdge> Edges { get; init; } = [];

    public IReadOnlyList<MatrixLabel> EdgeLabels { get; init; } = [];

    /// <summary>A legend of its own, spaced to fit its labels; when null the default legend is drawn.</summary>
    public IReadOnlyList<LegendItem>? Legend { get; init; }

    /// <summary>A spec with a lot to say per box may make its columns bigger.</summary>
    public Num ColumnWidth { get; init; } = 244;

    public Num BoxHeight { get; init; } = 94;
}

/// <summary>Norvia's Excalidraw matrix style: rows are applications, columns are process steps.</summary>
public static class MatrixDiagram
{
    private const int M = 40;
    private const int LabelWidth = 210;
    private const int Gap = 16;
    private const int Pad = 16;
    private const int LaneGap = 8;
    private const int GridLeft = M + LabelWidth + 12;
    private const int CrossHeight = 92;

    public static void Render(MatrixSpec spec, string excalidrawPath, string? svgPath = null)
    {
        Num colW = spec.ColumnWidth;
        Num boxW = colW - 36;
        Num boxH = spec.BoxHeight;

        var lanes = spec.Lanes.ToDictionary(l => l.Key);
        var cols = spec.Bands.SelectMany(s => s.Cols).ToList();
        var steps = spec.Steps.ToDictionary(s => s.Id);

        var slots = lanes.Keys.ToDictionary(k => k, _ => 1);
        foreach (var step in spec.Steps)
        {
            slots[step.Lane] = Math.Max(slots[step.Lane], step.Slot + 1);
        }

        Num streamY = 130, streamH = 34, colhY = 170, colhH = 62;
        Num y = colhY + colhH + 14;
        var laneY = new Dictionary<string, Num>();
        var laneH = new Dictionary<string, Num>();
        foreach (var lane in spec.Lanes)
        {
            var n = slots[lane.Key];
            laneH[lane.Key] = (2 * Pad) + (n * boxH) + ((n - 1) * Gap);
            laneY[lane.Key] = y;
            y = y + laneH[lane.Key] + LaneGap;
        }

        var crossY = new List<Num>();
        y += 10;
        foreach (var _ in spec.Cross)
        {
            crossY.Add(y);
            y = y + CrossHeight + LaneGap;
        }

        var footY = y + 18;
        var right = GridLeft + (cols.Count * colW);
        var totalW = right + 20;
        var totalH = footY + 50;

        Box BoxRect(string sid)
        {
            var s = steps[sid];
            return new Box(GridLeft + (s.Column * colW) + ((colW - boxW) / 2), laneY[s.Lane] + Pad + (s.Slot * (boxH + Gap)), boxW, boxH);
        }

        var cv = new Canvas();

        // title and legend
        cv.Text(M, 30, 1500, spec.Title, 38, Canvas.Ink, bold: true);
        cv.Text(M, 82, 1500, spec.Subtitle, 16, Canvas.Muted);
        if (spec.Legend is { } own)
        {
            var widths = own.Select(l => 34 + (l.Label.Length * 8.6) + 40).ToList();
            Num lx = totalW - 20 - Num.Sum(widths.Select(w => (Num)w)) + 40;
            for (var i = 0; i < own.Count; i++)
            {
                var item = own[i];
                cv.Rect(lx, 44, 26, 18, item.Fill, item.Stroke, 2, item.Dashed);
                cv.Text(lx + 34, 45, (item.Label.Length * 8.6) + 6, item.Label, 14, Canvas.Ink);
                lx += widths[i];
            }
        }
        else
        {
            var legend = Palette.DefaultLegend;
            Num lx = totalW - 20 - (legend.Count * 196);
            for (var i = 0; i < legend.Count; i++)
            {
                var item = legend[i];
                cv.Rect(lx + (i * 196), 44, 26, 18, item.Fill, item.Stroke, 2, item.Dashed);
                cv.Text(lx + (i * 196) + 34, 45, 156, item.Label, 14, Canvas.Ink);
            }
        }

        // value streams and column headers
        var c0 = 0;
        foreach (var stream in spec.Bands)
        {
            var colour = Palette.Of(stream.Palette).Label;
            var tint = Palette.StreamTint[stream.Palette];
            Num x0 = GridLeft + (c0 * colW) + 6;
            Num w = (stream.Cols.Count * colW) - 12;
            cv.Rect(x0, streamY, w, streamH, colour, colour);
            cv.Text(x0, streamY + 6, w, stream.Name.ToUpperInvariant(), 15, "#ffffff", "center", bold: true);
            for (var i = 0; i < stream.Cols.Count; i++)
            {
                Num cx = GridLeft + ((c0 + i) * colW) + 6;
                cv.Rect(cx, colhY, colW - 12, colhH, tint, colour, 2);
                cv.Text(cx, colhY + 10, colW - 12, stream.Cols[i].Name, 18, Canvas.Ink, "center", bold: true);
                cv.Text(cx, colhY + 36, colW - 12, stream.Cols[i].Sub, 13, Canvas.Muted, "center");
            }

            c0 += stream.Cols.Count;
        }

        // lanes and cross-cutting lanes
        void LaneFrame(Num y0, Num h, string name, string sub, string colour, string tint, bool dashed = false)
        {
            cv.Rect(M, y0, right - M, h, tint, "#e5e7eb", 1, dashed);
            cv.Rect(M, y0, LabelWidth, h, colour, colour, 1, dashed);
            cv.Text(M + 16, y0 + (h / 2) - 28, LabelWidth - 32, name, name.Length > 17 ? 18 : 21, "#ffffff", bold: true);
            cv.Text(M + 16, y0 + (h / 2) + 2, LabelWidth - 32, sub, 13, "#ffffff", wrapWidth: LabelWidth - 32);
        }

        foreach (var lane in spec.Lanes)
        {
            var tone = Palette.Of(lane.Palette);
            LaneFrame(laneY[lane.Key], laneH[lane.Key], lane.Name, lane.Sub, tone.Label, tone.Tint, lane.Dashed);
        }

        for (var i = 0; i < spec.Cross.Count; i++)
        {
            var c = spec.Cross[i];
            var y0 = crossY[i];
            var tone = Palette.Of(c.Palette);
            LaneFrame(y0, CrossHeight, c.Name, c.Sub, tone.Label, tone.Tint);
            Num bx = GridLeft + 8;
            Num bw = right - GridLeft - 20;
            cv.Rect(bx, y0 + 14, bw, CrossHeight - 28, "#ffffff", tone.Label, 2);
            cv.Text(bx + 20, y0 + 24, bw - 40, c.Headline, 17, Canvas.Ink, bold: true);
            cv.Text(bx + 20, y0 + 50, bw - 40, c.Detail, 14, Canvas.Muted);
        }

        // step boxes
        var boxElement = new Dictionary<string, Element>();
        var bounds = new Dictionary<string, Box>();
        foreach (var s in spec.Steps)
        {
            var b = BoxRect(s.Id);
            var tone = Palette.Of(lanes[s.Lane].Palette);
            var (fill, stroke, dashed) = Palette.ForKind(s.Kind, tone);
            string[] g = [$"grp-{s.Id}"];
            boxElement[s.Id] = cv.Rect(b.X, b.Y, b.W, b.H, fill, stroke, 2, dashed, g, $"box-{s.Id}");
            bounds[s.Id] = b;
            var th = cv.Text(b.X + 12, b.Y + 11, b.W - 24, s.Title, 15, s.Kind == "planned" ? Canvas.Muted : Canvas.Ink, "center", g, b.W - 30, true);
            cv.Text(b.X + 12, b.Y + 11 + th + 4, b.W - 24, s.Sub, 12, Canvas.Soft, "center", g, b.W - 24);
        }

        // arrows
        Pt Anchor(string sid, string side, Num dy)
        {
            var b = bounds[sid];
            return side switch
            {
                "r" => new Pt(b.X + b.W, b.Y + (b.H / 2) + dy),
                "l" => new Pt(b.X, b.Y + (b.H / 2) + dy),
                "t" => new Pt(b.X + (b.W / 2), b.Y),
                _ => new Pt(b.X + (b.W / 2), b.Y + b.H),
            };
        }

        foreach (var e in spec.Edges)
        {
            Num off = e.Off ?? 9, sdy = e.Sdy ?? 0, ddy = e.Ddy ?? 0;
            List<Pt> pts;
            if (e.Route == "h")
            {
                var a = Anchor(e.From, "r", sdy);
                var b = Anchor(e.To, "l", ddy);
                pts = Num.Abs(a.Y - b.Y) < 1 ? [a, b] : [a, new Pt(a.X + 18, a.Y), new Pt(a.X + 18, b.Y), b];
            }
            else if (e.Route == "v")
            {
                var down = bounds[e.To].Y > bounds[e.From].Y;
                pts = [Anchor(e.From, down ? "b" : "t", 0), Anchor(e.To, down ? "t" : "b", 0)];
            }
            else
            {
                // gr / gl: leave and enter on the same side, running in the gutter between columns
                var side = e.Route == "gr" ? "r" : "l";
                var a = Anchor(e.From, side, sdy);
                var b = Anchor(e.To, side, ddy);
                var gx = e.Route == "gr" ? a.X + off : a.X - off;
                pts = [a, new Pt(gx, a.Y), new Pt(gx, b.Y), b];
            }

            var arrow = cv.DrawArrow(pts, e.Dashed ? Canvas.ArrowPlanned : Canvas.Arrow, e.Dashed, e.Both, $"box-{e.From}", $"box-{e.To}");
            foreach (var sid in new[] { e.From, e.To })
            {
                boxElement[sid].BoundElements.Add(new Dictionary<string, object?> { ["id"] = arrow.Id, ["type"] = "arrow" });
            }
        }

        foreach (var label in spec.EdgeLabels)
        {
            var b = bounds[label.Step];
            cv.Text(b.X + label.Dx, b.Y + (b.H / 2) + label.Dy, 70, label.Text, 12, Canvas.Arrow, "center", bold: true);
        }

        cv.Text(M, footY, 2600, spec.Footnote, 13, Canvas.Muted);
        cv.Save(totalW, totalH, excalidrawPath, svgPath);
    }
}
