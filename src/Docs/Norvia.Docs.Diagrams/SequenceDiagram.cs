namespace Norvia.Docs.Diagrams;

/// <summary>A participant across the top; <c>Kind</c> "person" draws a person in yellow.</summary>
public sealed record SequenceParticipant(string Id, string Title, string Palette = "violet", string Sub = "", string? Kind = null);

/// <summary>A band over consecutive participants, such as the Flow they belong to.</summary>
public sealed record SequenceGroup(string Label, IReadOnlyList<string> Ids);

/// <summary>One step of the flow, in time order.</summary>
public abstract record SequenceStep;

/// <summary>A band that names a phase.</summary>
public sealed record Phase(string Title) : SequenceStep;

/// <summary>An arrow between two participants; dashed for a reply or an event.</summary>
public sealed record Message(string From, string To, string Label, bool Dashed = false, bool Both = false) : SequenceStep;

/// <summary>Something a participant does on its own.</summary>
public sealed record SelfStep(string Id, string Label) : SequenceStep;

/// <summary>A note across the participants it names.</summary>
public sealed record Note(IReadOnlyList<string> Ids, string Text) : SequenceStep;

/// <summary>The sequence style's spec; see README.md.</summary>
public sealed record SequenceSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Footnote { get; init; }

    public required IReadOnlyList<LegendItem> Legend { get; init; }

    public required IReadOnlyList<SequenceParticipant> Participants { get; init; }

    public IReadOnlyList<SequenceGroup> Groups { get; init; } = [];

    public required IReadOnlyList<SequenceStep> Steps { get; init; }
}

/// <summary>Norvia's Excalidraw sequence style: participants across the top, time running down, one arrow per message.</summary>
public static class SequenceDiagram
{
    private const int M = 40;
    private const int Top = 160;

    public static void Render(SequenceSpec spec, string excalidrawPath, string? svgPath = null)
    {
        var cv = new Canvas();
        var parts = spec.Participants;
        var n = parts.Count;
        Num colW = 200, headW = 168;

        // a name that no wrapping can break (such as an event's) widens the columns instead of overflowing its box
        if (parts.SelectMany(p => p.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(w => w.Length).DefaultIfEmpty(0).Max() > 19)
        {
            (colW, headW) = (226, 200);
        }

        var xs = new Dictionary<string, Num>();
        for (var i = 0; i < n; i++)
        {
            xs[parts[i].Id] = M + (colW / 2) + (i * colW);
        }

        var last = parts[^1].Id;
        Num rightPad = spec.Steps.Any(st => st is SelfStep s && s.Id == last) ? (colW * 1.5).Truncate() : 0;
        var width = Num.Max((M * 2) + (n * colW) + rightPad, ((Num)Canvas.GuessWidth(spec.Title, 38)).Truncate() + (2 * M));

        // first pass: heights
        // header boxes grow to fit a long hop name; flow groups, if any, get a band above them
        Num headH = 66;
        foreach (var p in parts)
        {
            var th = Canvas.TextHeight(p.Title, 14, headW - 24);
            Num sh = p.Sub.Length > 0 ? Canvas.TextHeight(p.Sub, 11, headW - 20) : 0;
            headH = Num.Max(headH, 10 + th + (sh != 0 ? 3 : 0) + sh + 10);
        }

        headH = headH.Truncate();
        Num gy = spec.Groups.Count > 0 ? 30 : 0;
        var headTop = Top + gy;
        var y = headTop + headH + 24;
        var rows = new List<(Num Y, Num H)>();
        foreach (var step in spec.Steps)
        {
            Num h, gap;
            switch (step)
            {
                case Phase:
                    (h, gap) = (30, 20);
                    break;
                case Message m:
                    {
                        var span = Num.Abs(xs[m.From] - xs[m.To]) - 20;
                        var lines = Canvas.Wrap(m.Label, 12, Num.Max(span, 120));
                        (h, gap) = (18 + (lines.Count * 15), 18);
                        break;
                    }

                case SelfStep s:
                    {
                        var lines = Canvas.Wrap(s.Label, 12, colW * 1.6);
                        (h, gap) = (Num.Max(34, 14 + (lines.Count * 15)), 16);
                        break;
                    }

                case Note note:
                    {
                        var span = Num.Abs(xs[note.Ids[0]] - xs[note.Ids[^1]]) + headW - 20;
                        var lines = Canvas.Wrap(note.Text, 12, Num.Max(span, 160));
                        (h, gap) = (18 + (lines.Count * 15), 18);
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown step {step}.");
            }

            rows.Add((y, h));
            y = y + h + gap;
        }

        var height = y + 70;

        cv.Text(M, 30, width - (2 * M), spec.Title, 38, Canvas.Ink, bold: true);
        cv.Text(M, 82, width - (2 * M), spec.Subtitle, 16, Canvas.Muted, wrapWidth: width - (2 * M));
        Num lx = M;
        foreach (var item in spec.Legend)
        {
            cv.Rect(lx, 128, 26, 18, item.Fill, item.Stroke, 2, item.Dashed);
            cv.Text(lx + 34, 129, (item.Label.Length * 8.6) + 6, item.Label, 14, Canvas.Ink);
            lx += 34 + (item.Label.Length * 8.6) + 40;
        }

        // lifelines first, so the rest sits on top
        foreach (var p in parts)
        {
            var x = xs[p.Id];
            cv.DrawArrow([new Pt(x, headTop + headH), new Pt(x, height - 60)], "#ced4da", true, false, null, null, plain: true);
        }

        foreach (var p in parts)
        {
            var tone = Palette.Of(p.Palette);
            var (fill, stroke) = p.Kind == "person" ? Palette.Person : (tone.Fill, tone.Stroke);
            var x = xs[p.Id];
            cv.Rect(x - (headW / 2), headTop, headW, headH, fill, stroke, 2);
            var th = cv.Text(x - (headW / 2) + 10, headTop + 10, headW - 20, p.Title, 14, Canvas.Ink, "center", wrapWidth: headW - 24, bold: true);
            if (p.Sub.Length > 0)
            {
                cv.Text(x - (headW / 2) + 10, headTop + 10 + th + 3, headW - 20, p.Sub, 11, Canvas.Soft, "center", wrapWidth: headW - 20);
            }
        }

        foreach (var g in spec.Groups)
        {
            var x1 = Num.Min(g.Ids.Select(i => xs[i])) - (headW / 2);
            var x2 = Num.Max(g.Ids.Select(i => xs[i])) + (headW / 2);
            cv.Rect(x1, Top, x2 - x1, 24, "#f1f3f5", "#adb5bd", 1, radius: 6);
            cv.Text(x1 + 8, Top + 4, x2 - x1 - 16, g.Label, 12, Canvas.Muted, "center", wrapWidth: x2 - x1 - 16, bold: true);
        }

        for (var i = 0; i < spec.Steps.Count; i++)
        {
            var (ry, h) = rows[i];
            switch (spec.Steps[i])
            {
                case Phase phase:
                    cv.Rect(M, ry, width - (2 * M), h, "#f1f3f5", "#dee2e6", 1);
                    cv.Text(M + 14, ry + 6, width - (2 * M) - 28, phase.Title, 14, Canvas.Ink, bold: true);
                    break;
                case Message m:
                    {
                        var xa = xs[m.From];
                        var xb = xs[m.To];
                        var lines = Canvas.Wrap(m.Label, 12, Num.Max(Num.Abs(xa - xb) - 20, 120));
                        var ay = ry + h - 8;
                        var direction = xb > xa ? 1 : -1;
                        cv.DrawArrow([new Pt(xa + (4 * direction), ay), new Pt(xb - (4 * direction), ay)], m.Dashed ? Canvas.ArrowPlanned : Canvas.Arrow, m.Dashed, m.Both, null, null);
                        var lw = lines.Max(l => l.Length) * 12 * 0.57;
                        cv.Rect(((xa + xb) / 2) - (lw / 2) - 6, ry + 1, lw + 12, (lines.Count * 15) + 2, "#ffffff", "#ffffff", 0, radius: 4);
                        cv.Text(((xa + xb) / 2) - (lw / 2) - 6, ry + 2, lw + 12, string.Join("\n", lines), 12, Canvas.Ink, "center");
                        break;
                    }

                case SelfStep s:
                    {
                        var x = xs[s.Id];
                        cv.DrawArrow([new Pt(x + 4, ry + 8), new Pt(x + 34, ry + 8), new Pt(x + 34, ry + h - 6), new Pt(x + 4, ry + h - 6)], Canvas.Arrow, false, false, null, null);
                        var sl = Canvas.Wrap(s.Label, 12, colW * 1.6);
                        cv.Rect(x + 40, ry + 2, (sl.Max(l => l.Length) * 12 * 0.57) + 12, (sl.Count * 15) + 4, "#ffffff", "#ffffff", 0, radius: 4);
                        cv.Text(x + 44, ry + 4, colW * 1.6, s.Label, 12, Canvas.Ink, wrapWidth: colW * 1.6);
                        break;
                    }

                case Note note:
                    {
                        var x1 = Num.Min(note.Ids.Select(id => xs[id])) - (headW / 2);
                        var x2 = Num.Max(note.Ids.Select(id => xs[id])) + (headW / 2);
                        cv.Rect(x1, ry, x2 - x1, h, "#fff9db", "#f59f00", 1);
                        cv.Text(x1 + 8, ry + 8, x2 - x1 - 16, note.Text, 12, Canvas.Ink, "center", wrapWidth: x2 - x1 - 16);
                        break;
                    }
            }
        }

        cv.Text(M, height - 56, width - (2 * M), spec.Footnote, 13, Canvas.Muted, wrapWidth: width - (2 * M));
        cv.Save(width, height, excalidrawPath, svgPath);
    }
}
