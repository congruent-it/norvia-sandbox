namespace Norvia.Docs.Diagrams;

/// <summary>A node at a (column, row) position of the grid. <c>Kind</c> is person, planned, or null for a system.</summary>
public sealed record GraphNode
{
    public required string Id { get; init; }

    public required Num Col { get; init; }

    public required Num Row { get; init; }

    public string Palette { get; init; } = "blue";

    public string? Kind { get; init; }

    public required string Title { get; init; }

    public string Sub { get; init; } = "";

    public Num? W { get; init; }
}

/// <summary>A boundary drawn around its members (nodes or other groups).</summary>
public sealed record GraphGroup
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Palette { get; init; } = "gray";

    public string? Sub { get; init; }

    public bool Dashed { get; init; }

    public required IReadOnlyList<string> Members { get; init; }
}

/// <summary>An arrow routed around the boxes, with an optional label.</summary>
public sealed record GraphEdge(string Src, string Dst, string Label = "")
{
    /// <summary>"h" or "v": the way the arrow runs; worked out from the positions when null.</summary>
    public string? Orient { get; init; }

    /// <summary>The side it leaves and the side it enters, each one of l, r, t, b.</summary>
    public (string From, string To)? Sides { get; init; }

    /// <summary>"top" or "bottom": loop round the outside of the drawing.</summary>
    public string? Via { get; init; }

    public bool Dashed { get; init; }

    public bool Both { get; init; }

    public bool Plain { get; init; }
}

/// <summary>The graph style's spec; see README.md.</summary>
public sealed record GraphSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Footnote { get; init; }

    public IReadOnlyList<LegendItem>? Legend { get; init; }

    public (Num Width, Num Height) Cell { get; init; } = (300, 140);

    public required IReadOnlyList<GraphNode> Nodes { get; init; }

    public IReadOnlyList<GraphGroup> Groups { get; init; } = [];

    public required IReadOnlyList<GraphEdge> Edges { get; init; }
}

/// <summary>Norvia's Excalidraw graph style: boxes in a grid, boundaries around them, orthogonal arrows.</summary>
public static class GraphDiagram
{
    private const int M = 40;
    private const int Top = 160;
    private const int NodeWidth = 232;
    private const int NodeMinHeight = 64;
    private const int Pad = 22;
    private const int PadTop = 48;
    private const int Clear = 6; // how far arrows keep from a box they pass

    private sealed class Node
    {
        public required GraphNode Spec { get; init; }

        public Num W { get; set; }

        public Num H { get; set; }

        public Num Cx { get; set; }

        public Num Cy { get; set; }

        public Num X { get; set; }

        public Num Y { get; set; }

        public Element? El { get; set; }
    }

    private sealed class Group
    {
        public required GraphGroup Spec { get; init; }

        public Box? Rect { get; set; }
    }

    private sealed class Edge
    {
        public required GraphEdge Spec { get; init; }

        public string Orient { get; set; } = "";

        public string SourceSide { get; set; } = "";

        public string TargetSide { get; set; } = "";

        public Pt Ps { get; set; }

        public Pt Pd { get; set; }

        public List<Pt> Path { get; set; } = [];
    }

    private readonly record struct Rect4(double X1, double Y1, double X2, double Y2);

    public static void Render(GraphSpec spec, string excalidrawPath, string? svgPath = null)
    {
        var cv = new Canvas();
        var (cw, rh) = spec.Cell;
        var nodes = spec.Nodes.ToDictionary(n => n.Id, n => new Node { Spec = n });

        // node geometry
        foreach (var n in nodes.Values)
        {
            n.W = n.Spec.W ?? NodeWidth;
            var th = Canvas.TextHeight(n.Spec.Title, 15, n.W - 30);
            Num sh = n.Spec.Sub.Length > 0 ? Canvas.TextHeight(n.Spec.Sub, 12, n.W - 24) : 0;
            n.H = Num.Max(NodeMinHeight, 11 + th + (n.Spec.Sub.Length > 0 ? 4 : 0) + sh + 11);
            n.Cx = (n.Spec.Col + 0.5) * cw;
            n.Cy = (n.Spec.Row + 0.5) * rh;
            n.X = n.Cx - (n.W / 2);
            n.Y = n.Cy - (n.H / 2);
        }

        // group boundaries, innermost first
        var groups = spec.Groups.ToDictionary(g => g.Id, g => new Group { Spec = g });

        Box Bounds(string gid)
        {
            var g = groups[gid];
            if (g.Rect is { } known)
            {
                return known;
            }

            var xs = new List<Num>();
            var ys = new List<Num>();
            var xe = new List<Num>();
            var ye = new List<Num>();
            foreach (var m in g.Spec.Members)
            {
                var b = groups.ContainsKey(m) ? Bounds(m) : new Box(nodes[m].X, nodes[m].Y, nodes[m].W, nodes[m].H);
                xs.Add(b.X);
                ys.Add(b.Y);
                xe.Add(b.X + b.W);
                ye.Add(b.Y + b.H);
            }

            var rect = new Box(Num.Min(xs) - Pad, Num.Min(ys) - PadTop, Num.Max(xe) - Num.Min(xs) + (2 * Pad), Num.Max(ye) - Num.Min(ys) + Pad + PadTop);
            g.Rect = rect;
            return rect;
        }

        foreach (var gid in groups.Keys)
        {
            Bounds(gid);
        }

        // translate everything so the drawing starts at the margin
        List<Box> AllBoxes() => [.. nodes.Values.Select(n => new Box(n.X, n.Y, n.W, n.H)), .. groups.Values.Select(g => g.Rect!.Value)];
        var all = AllBoxes();
        Num dx0 = M - Num.Min(all.Select(r => r.X));
        Num dy0 = Top - Num.Min(all.Select(r => r.Y));
        foreach (var n in nodes.Values)
        {
            n.X += dx0;
            n.Y += dy0;
            n.Cx += dx0;
            n.Cy += dy0;
        }

        foreach (var g in groups.Values)
        {
            var r = g.Rect!.Value;
            g.Rect = new Box(r.X + dx0, r.Y + dy0, r.W, r.H);
        }

        all = AllBoxes();
        var width = Num.Max(all.Select(r => r.X + r.W)) + M;
        var height = Num.Max(all.Select(r => r.Y + r.H)) + 90;

        // ----- routing -----
        var edges = spec.Edges.Select(e => new Edge { Spec = e }).ToList();
        foreach (var e in edges)
        {
            var a = nodes[e.Spec.Src];
            var b = nodes[e.Spec.Dst];
            var ddx = b.Cx - a.Cx;
            var ddy = b.Cy - a.Cy;
            e.Orient = e.Spec.Orient ?? (Num.Abs(ddx) < (a.W + b.W) / 2 * 0.55 ? "v" : "h");
            if (e.Spec.Sides is { } sides)
            {
                (e.SourceSide, e.TargetSide) = sides;
            }
            else if (e.Orient == "h")
            {
                (e.SourceSide, e.TargetSide) = ddx > 0 ? ("r", "l") : ("l", "r");
            }
            else
            {
                (e.SourceSide, e.TargetSide) = ddy > 0 ? ("b", "t") : ("t", "b");
            }
        }

        // spread the arrows that share one side of a box along that side
        var ports = new Dictionary<(string Node, string Side), List<(int Edge, bool IsSource, string Other)>>();
        void AddPort(string node, string side, (int, bool, string) entry)
        {
            if (!ports.TryGetValue((node, side), out var list))
            {
                ports[(node, side)] = list = [];
            }

            list.Add(entry);
        }

        for (var i = 0; i < edges.Count; i++)
        {
            AddPort(edges[i].Spec.Src, edges[i].SourceSide, (i, true, edges[i].Spec.Dst));
            AddPort(edges[i].Spec.Dst, edges[i].TargetSide, (i, false, edges[i].Spec.Src));
        }

        foreach (var ((nid, side), list) in ports)
        {
            var n = nodes[nid];
            var sorted = side is "l" or "r"
                ? list.OrderBy(t => nodes[t.Other].Cy).ToList()
                : list.OrderBy(t => nodes[t.Other].Cx).ToList();
            for (var k = 0; k < sorted.Count; k++)
            {
                var f = (k + 1) / (Num)(sorted.Count + 1);
                var p = side is "l" or "r"
                    ? new Pt(n.X + (side == "r" ? n.W : 0), n.Y + (n.H * f))
                    : new Pt(n.X + (n.W * f), n.Y + (side == "b" ? n.H : 0));
                if (sorted[k].IsSource)
                {
                    edges[sorted[k].Edge].Ps = p;
                }
                else
                {
                    edges[sorted[k].Edge].Pd = p;
                }
            }
        }

        var obstacles = nodes.ToDictionary(
            kv => kv.Key,
            kv => new Rect4(kv.Value.X.Value - Clear, kv.Value.Y.Value - Clear, kv.Value.X.Value + kv.Value.W.Value + Clear, kv.Value.Y.Value + kv.Value.H.Value + Clear));
        var placed = new List<List<(Pt A, Pt B)>>();

        static bool SegHits((Pt A, Pt B) seg, Rect4 rect)
        {
            double x1 = seg.A.X.Value, y1 = seg.A.Y.Value, x2 = seg.B.X.Value, y2 = seg.B.Y.Value;
            if (Math.Abs(x1 - x2) < 0.01)
            {
                return rect.X1 < x1 && x1 < rect.X2 && Math.Min(y1, y2) < rect.Y2 && Math.Max(y1, y2) > rect.Y1;
            }

            return rect.Y1 < y1 && y1 < rect.Y2 && Math.Min(x1, x2) < rect.X2 && Math.Max(x1, x2) > rect.X1;
        }

        double Cost(IReadOnlyList<Pt> path, HashSet<string> skip)
        {
            var segs = path.Zip(path.Skip(1), (a, b) => (A: a, B: b)).ToList();
            double c = 0;
            foreach (var seg in segs)
            {
                foreach (var (nid, r) in obstacles)
                {
                    if (!skip.Contains(nid) && SegHits(seg, r))
                    {
                        c += 10000;
                    }
                }

                double x1 = seg.A.X.Value, y1 = seg.A.Y.Value, x2 = seg.B.X.Value, y2 = seg.B.Y.Value;
                foreach (var other in placed)
                {
                    foreach (var o in other)
                    {
                        double u1 = o.A.X.Value, v1 = o.A.Y.Value, u2 = o.B.X.Value, v2 = o.B.Y.Value;
                        if (Math.Abs(x1 - x2) < 0.01 && Math.Abs(u1 - u2) < 0.01 && Math.Abs(x1 - u1) < 12)
                        {
                            if (Math.Min(y1, y2) < Math.Max(v1, v2) - 4 && Math.Max(y1, y2) > Math.Min(v1, v2) + 4)
                            {
                                c += 250;
                            }
                        }
                        else if (Math.Abs(y1 - y2) < 0.01 && Math.Abs(v1 - v2) < 0.01 && Math.Abs(y1 - v1) < 12)
                        {
                            if (Math.Min(x1, x2) < Math.Max(u1, u2) - 4 && Math.Max(x1, x2) > Math.Min(u1, u2) + 4)
                            {
                                c += 250;
                            }
                        }
                        else if (Math.Abs(x1 - x2) < 0.01 && Math.Abs(v1 - v2) < 0.01)
                        {
                            if (Math.Min(u1, u2) < x1 && x1 < Math.Max(u1, u2) && Math.Min(y1, y2) < v1 && v1 < Math.Max(y1, y2))
                            {
                                c += 12;
                            }
                        }
                        else if (Math.Abs(y1 - y2) < 0.01 && Math.Abs(u1 - u2) < 0.01)
                        {
                            if (Math.Min(v1, v2) < y1 && y1 < Math.Max(v1, v2) && Math.Min(x1, x2) < u1 && u1 < Math.Max(x1, x2))
                            {
                                c += 12;
                            }
                        }
                    }
                }
            }

            double length = 0;
            foreach (var (a, b) in segs)
            {
                length += Math.Abs(a.X.Value - b.X.Value) + Math.Abs(a.Y.Value - b.Y.Value);
            }

            c += (length * 0.01) + (segs.Count * 6);
            return c;
        }

        List<Pt> Route(Edge e)
        {
            var s = e.Ps;
            var d = e.Pd;
            var skip = new HashSet<string> { e.Spec.Src, e.Spec.Dst };
            var horizontal = e.Orient == "h";
            if (e.Spec.Via is { } via)
            {
                // loop round the outside of the drawing: out of the side, along a corridor above or below every box, then in
                var bottom = via == "bottom";
                var edgeY = bottom ? obstacles.Values.Max(o => o.Y2) : obstacles.Values.Min(o => o.Y1);
                var sign = e.SourceSide == "r" ? 1 : -1;
                double bestCost = double.NaN;
                List<Pt>? best = null;
                for (var dy = 14; dy < 200; dy += 6)
                {
                    var yy = bottom ? edgeY + dy : edgeY - dy;
                    for (var dx = 14; dx < 90; dx += 6)
                    {
                        var gx = s.X + (sign * dx);
                        List<Pt> path = [s, new Pt(gx, s.Y), new Pt(gx, yy), new Pt(d.X, yy), d];
                        var k = Cost(path, skip);
                        if (best is null || k < bestCost)
                        {
                            bestCost = k;
                            best = path;
                        }
                    }
                }

                return best!;
            }

            // transpose so one routine serves both directions; a transpose is its own inverse
            Pt T(Pt p) => horizontal ? p : new Pt(p.Y, p.X);
            var ts = T(s);
            var td = T(d);
            var cands = new List<List<Pt>>();
            if (Num.Abs(ts.Y - td.Y) < 1)
            {
                cands.Add([ts, td]);
            }

            var (lo, hi) = td.X < ts.X ? (td.X, ts.X) : (ts.X, td.X);

            Num step = 6;
            Num x = lo + 10;
            var sgn = td.X > ts.X ? 1 : -1;
            while (x < hi - 10)
            {
                cands.Add([ts, new Pt(x, ts.Y), new Pt(x, td.Y), td]);
                x += step;
            }

            // detour: leave, run along a corridor, come back (for a box in the way)
            var ylo = Num.Min(ts.Y, td.Y) - 260;
            var yhi = Num.Max(ts.Y, td.Y) + 260;
            var m1 = ts.X + (sgn * 14);
            var m2 = td.X - (sgn * 14);
            var y = ylo;
            while (y <= yhi)
            {
                cands.Add([ts, new Pt(m1, ts.Y), new Pt(m1, y), new Pt(m2, y), new Pt(m2, td.Y), td]);
                y += 8;
            }

            double bestK = double.NaN;
            List<Pt>? bestPath = null;
            foreach (var c in cands)
            {
                var pts = c.Select(T).ToList();
                var k = Cost(pts, skip) + (c.Count == 6 ? 60 : 0);
                if (bestPath is null || k < bestK)
                {
                    bestK = k;
                    bestPath = pts;
                }
            }

            return bestPath!;
        }

        // route the shorter arrows first, so long ones go around them
        var order = Enumerable.Range(0, edges.Count)
            .OrderBy(i => Math.Abs(edges[i].Ps.X.Value - edges[i].Pd.X.Value) + Math.Abs(edges[i].Ps.Y.Value - edges[i].Pd.Y.Value))
            .ToList();
        foreach (var i in order)
        {
            edges[i].Path = Route(edges[i]);
            placed.Add([.. edges[i].Path.Zip(edges[i].Path.Skip(1), (a, b) => (a, b))]);
        }

        // corridors can run outside the boxes: make room for them above the title line and below the footer
        var allPoints = edges.SelectMany(e => e.Path).ToList();
        Num minY = allPoints.Count > 0 ? Num.Min(allPoints.Select(p => p.Y)) : Top;
        var shift = Num.Max(0, Top - 10 - minY);
        if (shift != 0)
        {
            foreach (var n in nodes.Values)
            {
                n.Y += shift;
                n.Cy += shift;
            }

            foreach (var g in groups.Values)
            {
                var r = g.Rect!.Value;
                g.Rect = new Box(r.X, r.Y + shift, r.W, r.H);
            }

            foreach (var e in edges)
            {
                e.Path = [.. e.Path.Select(p => new Pt(p.X, p.Y + shift))];
            }
        }

        width = Num.Max(width, (allPoints.Count > 0 ? Num.Max(allPoints.Select(p => p.X)) : 0) + M);
        var shiftedPoints = edges.SelectMany(e => e.Path).ToList();
        height = Num.Max(height, (shiftedPoints.Count > 0 ? Num.Max(shiftedPoints.Select(p => p.Y)) : -90) + 90);

        // ----- drawing -----
        cv.Text(M, 30, 1600, spec.Title, 38, Canvas.Ink, bold: true);
        var subH = cv.Text(M, 82, width - (2 * M), spec.Subtitle, 16, Canvas.Muted, wrapWidth: width - (2 * M));
        var legendY = (82 + subH + 10).Truncate();
        Num lx = M;
        foreach (var item in spec.Legend ?? Palette.DefaultLegend)
        {
            cv.Rect(lx, legendY, 26, 18, item.Fill, item.Stroke, 2, item.Dashed);
            cv.Text(lx + 34, legendY + 1, (item.Label.Length * 8.6) + 6, item.Label, 14, Canvas.Ink);
            lx += 34 + (item.Label.Length * 8.6) + 40;
        }

        foreach (var g in groups.Values.OrderBy(g => -(g.Rect!.Value.W.Value * g.Rect.Value.H.Value)))
        {
            var tone = Palette.Of(g.Spec.Palette);
            var r = g.Rect!.Value;
            cv.Rect(r.X, r.Y, r.W, r.H, tone.Tint, tone.Stroke, 1.5, g.Spec.Dashed, radius: 14);
            cv.Text(r.X + 16, r.Y + 12, r.W - 32, g.Spec.Title, 14, tone.Label, bold: true);
            if (!string.IsNullOrEmpty(g.Spec.Sub))
            {
                cv.Text(r.X + 16, r.Y + 30, r.W - 32, g.Spec.Sub, 12, Canvas.Muted);
            }
        }

        foreach (var n in nodes.Values)
        {
            var tone = Palette.Of(n.Spec.Palette);
            var kind = n.Spec.Kind ?? "auto";
            var (fill, stroke, dashed) = Palette.ForKind(kind, tone);
            string[] gid = [$"grp-{n.Spec.Id}"];
            n.El = cv.Rect(n.X, n.Y, n.W, n.H, fill, stroke, 2, dashed, gid, $"box-{n.Spec.Id}");
            var th = cv.Text(n.X + 12, n.Y + 11, n.W - 24, n.Spec.Title, 15, kind == "planned" ? Canvas.Muted : Canvas.Ink, "center", gid, n.W - 30, true);
            if (n.Spec.Sub.Length > 0)
            {
                cv.Text(n.X + 12, n.Y + 11 + th + 4, n.W - 24, n.Spec.Sub, 12, Canvas.Soft, "center", gid, n.W - 24);
            }
        }

        foreach (var e in edges)
        {
            var arrow = cv.DrawArrow(e.Path, e.Spec.Dashed ? Canvas.ArrowPlanned : Canvas.Arrow, e.Spec.Dashed, e.Spec.Both, $"box-{e.Spec.Src}", $"box-{e.Spec.Dst}", e.Spec.Plain);
            foreach (var nid in new[] { e.Spec.Src, e.Spec.Dst })
            {
                nodes[nid].El!.BoundElements.Add(new Dictionary<string, object?> { ["id"] = arrow.Id, ["type"] = "arrow" });
            }
        }

        var labelRects = new List<Rect4>();
        var nodeRects = nodes.Values.Select(n => new Rect4(n.X.Value, n.Y.Value, (n.X + n.W).Value, (n.Y + n.H).Value)).ToList();
        nodeRects.AddRange(groups.Values.Select(g => new Rect4(g.Rect!.Value.X.Value, g.Rect.Value.Y.Value, (g.Rect.Value.X + g.Rect.Value.W).Value, (g.Rect.Value.Y + 46).Value)));
        static bool Overlap(Rect4 r, Rect4 q) => r.X1 < q.X2 && r.X2 > q.X1 && r.Y1 < q.Y2 && r.Y2 > q.Y1;

        // labels last, so they sit on top of the lines
        foreach (var e in edges.OrderBy(e => e.Spec.Label.Length))
        {
            if (e.Spec.Label.Length == 0)
            {
                continue;
            }

            var lines = Canvas.Wrap(e.Spec.Label, 12, 170);
            Num lw = (lines.Max(l => l.Length) * 12 * 0.57) + 12;
            Num lh = lines.Count * 12 * 1.25;
            var segs = e.Path.Zip(e.Path.Skip(1), (a, b) => (A: a, B: b))
                .OrderBy(s => -(Math.Abs(s.A.X.Value - s.B.X.Value) + Math.Abs(s.A.Y.Value - s.B.Y.Value)))
                .ToList();
            double[] fractions = [0.5, 0.3, 0.7, 0.15, 0.85];
            var spots = segs.SelectMany(s => fractions.Select(f => new Pt(s.A.X + ((s.B.X - s.A.X) * f), s.A.Y + ((s.B.Y - s.A.Y) * f)))).ToList();
            var chosen = spots[0];
            Rect4 Around(Pt p) => new((p.X - (lw / 2) - 12).Value, (p.Y - (lh / 2) - 6).Value, (p.X + (lw / 2) + 12).Value, (p.Y + (lh / 2) + 6).Value);
            foreach (var spot in spots)
            {
                var r = Around(spot);
                if (!nodeRects.Concat(labelRects).Any(q => Overlap(r, q)))
                {
                    chosen = spot;
                    break;
                }
            }

            labelRects.Add(Around(chosen));
            cv.Rect(chosen.X - (lw / 2), chosen.Y - (lh / 2) - 2, lw, lh + 4, "#ffffff", "#ffffff", 0, radius: 4);
            cv.Text(chosen.X - (lw / 2), chosen.Y - (lh / 2), lw, string.Join("\n", lines), 12, Canvas.Arrow, "center");
        }

        cv.Text(M, height - 60, width - (2 * M), spec.Footnote, 13, Canvas.Muted, wrapWidth: width - (2 * M));
        cv.Save(width, height, excalidrawPath, svgPath);
    }
}
