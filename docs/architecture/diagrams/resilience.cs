#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// What happens when a hop fails, by failure pattern, and who finds out.
//
//     dotnet run docs/architecture/diagrams/resilience.cs --    # writes failure-patterns.excalidraw and .png here
//
// Matrix style (see README.md): the columns are the five ways a hop can fail, the rows are who finds out. Needs rsvg-convert for the PNG.

using Norvia.Docs.Diagrams;

var spec = new MatrixSpec
{
    Title = "Failure patterns",
    Subtitle = "What happens when a hop fails, and who finds out. Every hop of every Flow fails in one of five ways, and no alert is built yet, so a failure that reaches nobody waits to be found in the Integration console.",
    ColumnWidth = 300,
    BoxHeight = 132,
    Legend = [new LegendItem("Someone sees it in an application", "#fff3bf", "#f59f00", false), new LegendItem("The console shows it", "#ffdeeb", "#d6336c", false), new LegendItem("A caller is told", "#e9ecef", "#868e96", false), new LegendItem("What is missing", "#f1f3f5", "#adb5bd", true)],
    Footnote = "Solid yellow is a person who sees it in an application. Dashed grey is what is missing: every alert is planned, none is built. Source of truth: docs/architecture/failure-behaviour.md, which says it per hop.",
    Lanes = [
        new MatrixLane("caller", "gray", "The caller", "A partner, told in the same call"),
        new MatrixLane("owner", "blue", "The owner's user", "Planner, Finance or the Driver, in the application"),
        new MatrixLane("operator", "pink", "Integration operator", "In the console: the trail, failed runs, dead-letter queues"),
        new MatrixLane("nobody", "lightgray", "Nobody", "No alert is built", Dashed: true),
    ],
    Bands = [
        new MatrixBand("From a partner", "blue", [new("Partner call", "A partner waits: P1 · P4 · E2 · P8"), new("Queued message", "A partner is told 202: P5 · P6")]),
        new MatrixBand("Between applications", "green", [new("Sent, then reported", "Logic App sends out: P4 · P7 · P11 · E1 · E2"), new("Recorded by an owner", "Logic App calls the API: P9 · E1 · E2 · P12")]),
        new MatrixBand("Events", "orange", [new("Event published", "Every outbox")]),
    ],
    Steps = [
        new MatrixStep("a1", 0, "caller", 0, "auto", "Told in the same call", "The partner is told and sends again. Intake and answers retry the TMS three times first, and a 422 is an answer, not a failure"),
        new MatrixStep("a2", 0, "operator", 0, "auto", "View the run and the trail", "View only: a resubmit would create an order the Shipper believes was never received"),
        new MatrixStep("a3", 0, "nobody", 0, "planned", "No alert on failed runs", "Planned: failed requests on the Partner API, failed Logic App runs"),
        new MatrixStep("b1", 1, "caller", 0, "auto", "Told 202, and nothing more", "The Carrier is told at once, and never hears if recording fails later"),
        new MatrixStep("b2", 1, "operator", 0, "auto", "Dead-letter queue", "After 10 deliveries, or at once if unmappable. Resubmit to the queue, or discard"),
        new MatrixStep("b3", 1, "nobody", 0, "planned", "No alert on the dead-letter queue", "A delivered status that never maps leaves the order In transit: the headline incident"),
        new MatrixStep("c1", 2, "owner", 0, "person", "Sees Not delivered", "Booking, notification, cancellation or Trip version, or an invoice. Send it again, or phone the Carrier"),
        new MatrixStep("c2", 2, "operator", 0, "auto", "A failed run, if the report fails", "The item stays Requested or Issued and the run fails"),
        new MatrixStep("c3", 2, "nobody", 0, "planned", "No alert on failed runs", "The owner's list is the signal"),
        new MatrixStep("d1", 3, "owner", 0, "person", "The Driver sees the report's state", "Driver reports only: Refused with the TMS's code, or Not recorded"),
        new MatrixStep("d2", 3, "operator", 0, "auto", "Dead-lettered with the owner's code", "Resubmit to that subscription once the cause is fixed. Billing: the order stays Billable with no line in the ERP until then"),
        new MatrixStep("d3", 3, "nobody", 0, "planned", "No alert on the dead-letter queue", "Planned: failed runs and the dead-letter queues"),
        new MatrixStep("e2", 4, "operator", 0, "auto", "A missing hop on the trail", "Retried every second, forever. A failed cycle holds back every message behind it"),
        new MatrixStep("e3", 4, "nobody", 0, "planned", "No alert on the oldest message", "Planned: the oldest unpublished outbox message"),
    ],
    Edges = [],
};

var output = Path.Combine(DiagramOutput.SpecDirectory(), "failure-patterns.excalidraw");
var svg = Path.Combine(Path.GetTempPath(), "failure-patterns.svg");
MatrixDiagram.Render(spec, output, svg);
DiagramOutput.ToPng(svg, Path.ChangeExtension(output, ".png"), Console.Out);
Console.WriteLine($"wrote {Path.GetFileName(output)}");
