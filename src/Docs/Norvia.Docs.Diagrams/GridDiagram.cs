namespace Norvia.Docs.Diagrams;

/// <summary>A column of the grid: a resource, in an application's colour.</summary>
public sealed record GridColumn(string Title, string Sub, string Palette = "gray");

/// <summary>A row: an identity.</summary>
public sealed record GridRow(string Id, string Title, string Sub);

/// <summary>A band of rows.</summary>
public sealed record GridGroup(string Title, string Palette, IReadOnlyList<GridRow> Rows);

/// <summary>One chip in a cell: what the row does with the column. <c>Kind</c> is role, read or write.</summary>
public sealed record GridChip(string Text, string Kind);

/// <summary>A legend entry of the grid style: the label and the chip kind it explains.</summary>
public sealed record GridLegend(string Label, string Kind);

/// <summary>The grid style's spec; see README.md.</summary>
public sealed record GridSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Footnote { get; init; }

    public required IReadOnlyList<GridLegend> Legend { get; init; }

    public required IReadOnlyList<GridColumn> Columns { get; init; }

    public required IReadOnlyList<GridGroup> Groups { get; init; }

    /// <summary>The chips of each cell, by row ID and column index.</summary>
    public required IReadOnlyDictionary<(string Row, int Column), IReadOnlyList<GridChip>> Cells { get; init; }
}

/// <summary>Norvia's Excalidraw grid style: who has what, as rows against columns.</summary>
public static class GridDiagram
{
    private const int M = 40;
    private const int Top = 160;
    private const int LabelWidth = 210;
    private const int ColumnWidth = 232;
    private const int ChipPad = 5;
    private const int ChipGap = 5;
    private const int RowPad = 8;

    private static readonly Dictionary<string, (string Fill, string Stroke)> Kinds = new()
    {
        ["role"] = ("#d0ebff", "#1c7ed6"),  // holds an API role: may call
        ["read"] = ("#d3f9d8", "#2f9e44"),  // receives, reads, lists
        ["write"] = ("#ffe8cc", "#e8590c"), // sends, writes, resubmits
    };

    public static void Render(GridSpec spec, string excalidrawPath, string? svgPath = null)
    {
        var cv = new Canvas();
        var cols = spec.Columns;
        var n = cols.Count;
        Num width = (M * 2) + LabelWidth + (n * ColumnWidth);

        Num ChipHeight(string t) => Canvas.TextHeight(t, 11, ColumnWidth - 12 - 16) + (2 * ChipPad);
        IReadOnlyList<GridChip> CellOf(string row, int column) => spec.Cells.TryGetValue((row, column), out var chips) ? chips : [];

        var rows = spec.Groups.SelectMany(g => g.Rows).ToList();
        var heights = new Dictionary<string, Num>();
        foreach (var r in rows)
        {
            var colHeights = Enumerable.Range(0, n).Select(c => Num.Sum(CellOf(r.Id, c).Select(chip => ChipHeight(chip.Text) + ChipGap))).ToList();
            heights[r.Id] = Num.Max(42, Num.Max(colHeights) + (RowPad * 2) - ChipGap);
        }

        Num y = Top + 80;
        var totalH = y + Num.Sum(heights.Values.Select(h => h + 6)) + (spec.Groups.Count * 42) + 80;
        cv.Text(M, 30, width - (2 * M), spec.Title, 38, Canvas.Ink, bold: true);
        var subH = cv.Text(M, 82, width - (2 * M), spec.Subtitle, 16, Canvas.Muted, wrapWidth: width - (2 * M));
        Num lx = M;
        var ly = (82 + subH + 10).Truncate();
        foreach (var item in spec.Legend)
        {
            var (fill, stroke) = Kinds[item.Kind];
            cv.Rect(lx, ly, 26, 18, fill, stroke, 2);
            cv.Text(lx + 34, ly + 1, (item.Label.Length * 8.6) + 6, item.Label, 14, Canvas.Ink);
            lx += 34 + (item.Label.Length * 8.6) + 40;
        }

        // column headers
        for (var c = 0; c < n; c++)
        {
            var col = cols[c];
            var tone = Palette.Of(col.Palette);
            Num x = M + LabelWidth + (c * ColumnWidth) + 4;
            cv.Rect(x, Top, ColumnWidth - 8, 66, tone.Label, tone.Label, 1);
            var th = cv.Text(x + 8, Top + 8, ColumnWidth - 24, col.Title, 15, "#ffffff", "center", wrapWidth: ColumnWidth - 28, bold: true);
            if (col.Sub.Length > 0)
            {
                cv.Text(x + 8, Top + 8 + th + 2, ColumnWidth - 24, col.Sub, 11, "#ffffff", "center", wrapWidth: ColumnWidth - 28);
            }
        }

        foreach (var g in spec.Groups)
        {
            var tone = Palette.Of(g.Palette);
            cv.Rect(M, y, width - (2 * M), 30, tone.Tint, tone.Stroke, 1);
            cv.Text(M + 12, y + 6, width - (2 * M) - 24, g.Title, 14, tone.Label, bold: true);
            y += 36;
            foreach (var r in g.Rows)
            {
                var h = heights[r.Id];
                cv.Rect(M, y, width - (2 * M), h, "#ffffff", "#e9ecef", 1, radius: 6);
                cv.Rect(M, y, LabelWidth - 6, h, tone.Fill, tone.Stroke, 1, radius: 6);
                var th = cv.Text(M + 10, y + 7, LabelWidth - 26, r.Title, 13, Canvas.Ink, wrapWidth: LabelWidth - 28, bold: true);
                if (r.Sub.Length > 0)
                {
                    cv.Text(M + 10, y + 7 + th + 1, LabelWidth - 26, r.Sub, 11, Canvas.Soft, wrapWidth: LabelWidth - 28);
                }

                for (var c = 0; c < n; c++)
                {
                    Num cy = y + RowPad;
                    foreach (var chip in CellOf(r.Id, c))
                    {
                        var (fill, stroke) = Kinds[chip.Kind];
                        var ch = ChipHeight(chip.Text);
                        Num cx = M + LabelWidth + (c * ColumnWidth) + 6;
                        cv.Rect(cx, cy, ColumnWidth - 12, ch, fill, stroke, 1, radius: 6);
                        cv.Text(cx + 8, cy + ChipPad, ColumnWidth - 28, chip.Text, 11, Canvas.Ink, wrapWidth: ColumnWidth - 28);
                        cy += ch + ChipGap;
                    }
                }

                y += h + 6;
            }

            y += 6;
        }

        cv.Text(M, totalH - 56, width - (2 * M), spec.Footnote, 13, Canvas.Muted, wrapWidth: width - (2 * M));
        cv.Save(width, totalH, excalidrawPath, svgPath);
    }
}
