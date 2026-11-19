namespace Norvia.Docs.Diagrams.UnitTests;

/// <summary>Each style draws the same text, byte for byte, that the Python generators drew for the same spec.</summary>
public sealed class StyleTests
{
    private static readonly MatrixSpec Matrix = new()
    {
        Title = "Matrix",
        Subtitle = "Rows are applications.",
        Footnote = "Source: tests.",
        Lanes =
        [
            new MatrixLane("a", "blue", "A longer application name", "Line one\nline two"),
            new MatrixLane("b", "lightgray", "B", "Planned", Dashed: true),
        ],
        Bands =
        [
            new MatrixBand("Stream & one", "blue", [new("First", "P1"), new("Second", "P2 · P3")]),
            new MatrixBand("Two", "green", [new("Third", "E1")]),
        ],
        Cross = [new MatrixCross("Console", "Operator", "pink", "Watches every hop", "Reads telemetry <only>.")],
        Steps =
        [
            new MatrixStep("s1", 0, "a", 0, "person", "Submit a booking", "P1 · Persona"),
            new MatrixStep("s2", 0, "a", 1, "auto", "Accept or reject", "P1 · API"),
            new MatrixStep("s3", 1, "a", 0, "auto", "Rate it", "P2"),
            new MatrixStep("s4", 1, "b", 0, "planned", "Later", "Not built"),
            new MatrixStep("s5", 2, "a", 0, "auto", "Invoice", "E1"),
            new MatrixStep("s6", 2, "b", 0, "person", "Correct", "E1"),
        ],
        Edges =
        [
            new MatrixEdge("s1", "s2", "v"),
            new MatrixEdge("s2", "s1", "v", Both: true),
            new MatrixEdge("s2", "s3", "h"),
            new MatrixEdge("s3", "s5", "gr", Off: 12, Sdy: 6),
            new MatrixEdge("s5", "s3", "gl", Dashed: true, Ddy: -4),
            new MatrixEdge("s4", "s6", "h", Dashed: true, Sdy: 5),
            new MatrixEdge("s3", "s4", "v"),
        ],
        EdgeLabels = [new MatrixLabel("Branch", "s2", -64, -22)],
    };

    private static readonly GraphSpec Graph = new()
    {
        Title = "Graph",
        Subtitle = "Boxes in a grid, with a subtitle that is long enough to need a second line when it wraps at the width.",
        Footnote = "Source of truth: the tests.",
        Legend = [Palette.LegendPerson("A person"), new LegendItem("Planned", "#f1f3f5", "#adb5bd", true)],
        Cell = (330, 130),
        Nodes =
        [
            new GraphNode { Id = "a", Col = 0, Row = 0, Kind = "person", Title = "Person", Sub = "Starts" },
            new GraphNode { Id = "b", Col = 1.5, Row = 0.2, Palette = "violet", W = 250, Title = "Integration layer", Sub = "[APIM]\nsrc/Docs/Norvia.Docs.Diagrams/Very/Long/Path/To/Something.cs and more" },
            new GraphNode { Id = "c", Col = 3, Row = 0, Palette = "blue", Title = "TMS" },
            new GraphNode { Id = "d", Col = 1.5, Row = 1.6, Palette = "green", Title = "ERP", Sub = "Master data" },
            new GraphNode { Id = "e", Col = 3, Row = 1.6, Kind = "planned", Title = "WMS" },
            new GraphNode { Id = "f", Col = 0, Row = 1.6, Title = "Plain" },
            new GraphNode { Id = "g", Col = 0, Row = 3, Palette = "teal", Title = "Loop target", Sub = "Run out and round" },
        ],
        Groups =
        [
            new GraphGroup { Id = "inner", Title = "Inner", Palette = "violet", Members = ["b", "d"] },
            new GraphGroup { Id = "outer", Title = "Outer", Palette = "orange", Sub = "With a sub", Dashed = true, Members = ["inner", "c"] },
        ],
        Edges =
        [
            new GraphEdge("a", "b", "books"),
            new GraphEdge("b", "c", "commands, queries [HTTPS]"),
            new GraphEdge("c", "d") { Orient = "v" },
            new GraphEdge("e", "b", "dashed") { Dashed = true, Sides = ("l", "b") },
            new GraphEdge("a", "d", "both ways") { Both = true },
            new GraphEdge("f", "g", "plain") { Plain = true },
            new GraphEdge("g", "c", "loops round") { Via = "bottom", Sides = ("r", "b") },
            new GraphEdge("c", "a", "back") { Via = "top", Sides = ("t", "t") },
        ],
    };

    private static readonly GridSpec Grid = new()
    {
        Title = "Grid",
        Subtitle = "Who may do what.",
        Footnote = "Source: tests.",
        Legend = [new GridLegend("Role", "role"), new GridLegend("Read", "read"), new GridLegend("Write", "write")],
        Columns = [new GridColumn("TMS API", "App roles", "blue"), new GridColumn("Bus", "", "violet"), new GridColumn("Plain", "")],
        Groups =
        [
            new GridGroup("Logic Apps", "violet", [new GridRow("r1", "intake", "Intake Logic App"), new GridRow("r2", "empty", "")]),
            new GridGroup("Apps", "blue", [new GridRow("r3", "a-long-application-name here", "With a sub")]),
        ],
        Cells = new Dictionary<(string Row, int Column), IReadOnlyList<GridChip>>
        {
            [("r1", 0)] = [new GridChip("TransportOrders.Intake", "role")],
            [("r1", 1)] = [new GridChip("Subscription x with a very long name that wraps", "read"), new GridChip("Another", "write")],
            [("r3", 0)] = [new GridChip("Own", "role")],
        },
    };

    private static readonly SequenceSpec Sequence = new()
    {
        Title = "Sequence",
        Subtitle = "A flow across several applications.",
        Footnote = "Source: tests.",
        Legend = [new LegendItem("Gray", "#e9ecef", "#868e96"), new LegendItem("Dashed", "#f1f3f5", "#adb5bd", true)],
        Groups = [new SequenceGroup("Flow one", ["a", "b"])],
        Participants =
        [
            new SequenceParticipant("a", "Shipper", "gray", "Partner portal", "person"),
            new SequenceParticipant("b", "API", "violet"),
            new SequenceParticipant("c", "TMS", "blue", "TMS API"),
        ],
        Steps =
        [
            new Phase("P1 · Intake"),
            new Message("a", "b", "POST a booking, in the partner's format"),
            new Message("b", "c", "A call across two columns with a label that is long enough to wrap onto a second line here", Dashed: true),
            new SelfStep("c", "Accepts it, in one transaction, and a long explanation that wraps"),
            new Message("c", "a", "Both ways", Both: true),
            new Note(["a", "c"], "A note across all three participants"),
            new Message("c", "b", "back left"),
        ],
    };

    [Fact]
    public void Render_Matrix_MatchesTheGolden() => Check("matrix", (o, s) => MatrixDiagram.Render(Matrix, o, s));

    [Fact]
    public void Render_MatrixWithItsOwnLegendAndBiggerBoxes_MatchesTheGolden() =>
        Check("matrix-own-legend", (o, s) => MatrixDiagram.Render(
            Matrix with
            {
                ColumnWidth = 300,
                BoxHeight = 110,
                Legend = [new LegendItem("Mine", "#fff3bf", "#f59f00"), new LegendItem("Dashed one", "#f1f3f5", "#adb5bd", true)],
            },
            o,
            s));

    [Fact]
    public void Render_Graph_MatchesTheGolden() => Check("graph", (o, s) => GraphDiagram.Render(Graph, o, s));

    [Fact]
    public void Render_GraphWithTheDefaultLegend_MatchesTheGolden() =>
        Check("graph-default-legend", (o, s) => GraphDiagram.Render(Graph with { Legend = null }, o, s));

    [Fact]
    public void Render_Grid_MatchesTheGolden() => Check("grid", (o, s) => GridDiagram.Render(Grid, o, s));

    [Fact]
    public void Render_Sequence_MatchesTheGolden() => Check("sequence", (o, s) => SequenceDiagram.Render(Sequence, o, s));

    [Fact]
    public void Render_SequenceWithAnUnbreakableName_WidensItsColumns() =>
        Check("sequence-wide", (o, s) => SequenceDiagram.Render(
            Sequence with
            {
                Groups = [],
                Participants = [new SequenceParticipant("x", "subscription-carrier-booking-something", "violet", "topic"), new SequenceParticipant("y", "Y", "gray")],
                Steps = [new Message("x", "y", "short"), new Message("y", "x", "reply", Dashed: true)],
            },
            o,
            s));

    [Fact]
    public void Render_GridChipOfAnUnknownKind_Throws()
    {
        var spec = Grid with { Cells = new Dictionary<(string Row, int Column), IReadOnlyList<GridChip>> { [("r1", 0)] = [new GridChip("x", "nonsense")] } };
        using var folder = new TempFolder();

        var act = () => GridDiagram.Render(spec, folder.Path("x.excalidraw"));

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Render_SequenceStepOfAnUnknownKind_Throws()
    {
        var spec = Sequence with { Steps = [new Unknown()] };
        using var folder = new TempFolder();

        var act = () => SequenceDiagram.Render(spec, folder.Path("x.excalidraw"));

        act.Should().Throw<InvalidOperationException>();
    }

    private static void Check(string name, Action<string, string> render)
    {
        using var folder = new TempFolder();
        render(folder.Path($"{name}.excalidraw"), folder.Path($"{name}.svg"));

        File.ReadAllText(folder.Path($"{name}.excalidraw")).Should().Be(Golden.Read($"{name}.excalidraw"));
        File.ReadAllText(folder.Path($"{name}.svg")).Should().Be(Golden.Read($"{name}.svg"));
    }

    private sealed record Unknown : SequenceStep;
}
