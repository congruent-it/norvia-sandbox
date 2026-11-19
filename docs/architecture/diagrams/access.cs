#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// Who may do what: the identity and access grid.
//
//     dotnet run docs/architecture/diagrams/access.cs -- [name ...]    # writes <name>.excalidraw and .png here; no names draws all
//
// Grid style (see README.md). The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

const string R = "role", D = "read", W = "write";
const int Tms = 0, Erp = 1, Drv = 2, Trg = 3, Bus = 4, Blob = 5, Kv = 6, Mon = 7;

GridColumn[] columns =
[
    new("TMS API", "App roles", "blue"),
    new("ERP API", "App roles", "green"),
    new("Driver app API", "App roles", "teal"),
    new("Logic App triggers", "App roles", "violet"),
    new("Service Bus", "Azure roles", "violet"),
    new("Blob storage", "Azure roles", "gray"),
    new("Key Vault", "Secrets User", "gray"),
    new("Telemetry and management", "Azure roles", "pink"),
];

static GridRow La(string id, string title, string sub) => new(id, title, sub);

GridRow[] logicApps =
[
    La("intake", "intake", "Intake Logic App, P1"), La("answer", "answer", "Carrier answer, P4"), La("booking", "booking", "Carrier booking, P4"),
    La("cancel", "carrier-cancellation", "Carrier cancellation, P7"), La("notify", "shipper-notification", "Shipper notification, P7"),
    La("dispatch", "trip-dispatch", "Trip dispatch, P11"), La("tracking", "tracking", "Carrier tracking, P5"),
    La("pod", "proof-of-delivery", "Carrier Proof of delivery, P6"), La("reports", "driver-reports", "Driver reports, P12"),
    La("billing", "billing", "Billing, E1"), La("cost", "carrier-cost", "Carrier cost, P9"), La("sinv", "shipper-invoice", "Shipper invoice, E1"),
    La("cinv", "carrier-invoice", "Carrier invoice, E2"), La("cout", "carrier-invoice-outcome", "Carrier invoice outcome, E2"),
];
GridRow[] apps =
[
    La("apim", "apim", "API Management"), La("tmsapi", "tms-api", "TMS API, owned by the AppHost"), La("tmsui", "tms-ui", "TMS UI"),
    La("erp", "erp-web, erp-api", "ERP UI and API, owned by the AppHost"), La("driver", "driver-app", "Driver app"), La("portal", "partner-portal", "Partner portal"),
    La("sim", "simulator", "Simulator"), La("console", "integration-console", "Integration console"),
];

Dictionary<(string Row, int Column), IReadOnlyList<GridChip>> cells = [];
void Put(string row, int col, params (string Text, string Kind)[] items) => cells[(row, col)] = [.. items.Select(i => new GridChip(i.Text, i.Kind))];

Put("intake", Tms, ("TransportOrders.Intake", R));
Put("answer", Tms, ("CarrierBookings.Answer", R));
Put("booking", Tms, ("CarrierBookings.Report", R)); Put("booking", Bus, ("Subscription carrier-booking", D)); Put("booking", Kv, ("Each Carrier's endpoint key", D));
Put("cancel", Tms, ("CarrierBookings.Report", R)); Put("cancel", Bus, ("Subscription carrier-cancellation", D)); Put("cancel", Kv, ("Each Carrier's endpoint key", D));
Put("notify", Tms, ("ShipperNotifications.Report", R)); Put("notify", Bus, ("Subscription shipper-notification", D)); Put("notify", Kv, ("Each Shipper's endpoint key", D));
Put("dispatch", Tms, ("Trips.ReportDelivery", R)); Put("dispatch", Bus, ("Subscription trip-dispatch", D)); Put("dispatch", Kv, ("The Driver app's endpoint key", D));
Put("tracking", Tms, ("TrackingEvents.Record", R)); Put("tracking", Bus, ("Queue carrier-tracking", D));
Put("pod", Tms, ("ProofsOfDelivery.Record", R)); Put("pod", Bus, ("Queue carrier-proof-of-delivery", D)); Put("pod", Blob, ("Inbound container: read and delete", W));
Put("reports", Tms, ("Trips.Start, Trips.End", R), ("TrackingEvents.Record, ProofsOfDelivery.Record", R)); Put("reports", Drv, ("DriverReports.ReportOutcome", R));
Put("reports", Bus, ("Subscription driver-reports", D)); Put("reports", Blob, ("Driver app's transit container: read and delete", W));
Put("billing", Erp, ("BillableLines.Record", R)); Put("billing", Bus, ("Subscription billing", D));
Put("cost", Erp, ("CarrierCosts.Record", R)); Put("cost", Bus, ("Subscription carrier-cost", D));
Put("sinv", Erp, ("ShipperInvoices.Report", R)); Put("sinv", Bus, ("Subscription shipper-invoice", D)); Put("sinv", Kv, ("Each Shipper's endpoint key", D));
Put("cinv", Erp, ("CarrierInvoices.Receive", R));
Put("cout", Erp, ("CarrierInvoices.Report", R)); Put("cout", Bus, ("Subscription carrier-invoice-outcome", D)); Put("cout", Kv, ("Each Carrier's endpoint key", D));
Put("apim", Tms, ("ShipperTracking.Read", R)); Put("apim", Trg, ("TransportOrderIntake.Trigger, CarrierAnswer.Trigger, CarrierInvoice.Trigger", R));
Put("apim", Bus, ("Queues carrier-tracking and carrier-proof-of-delivery: send", W)); Put("apim", Blob, ("Inbound container: write", W)); Put("apim", Mon, ("Application Insights: publish metrics", W));
Put("tmsapi", Bus, ("Namespace: send, and receive to peek the subscriptions a reset waits on", W)); Put("tmsapi", Blob, ("The TMS's storage: contributor", W));
Put("tmsui", Tms, ("TransportOrders.Manage", R)); Put("tmsui", Blob, ("The TMS's storage: reader", D));
Put("erp", Bus, ("Send, through the outbox", W), ("Receive to peek the subscriptions a reset waits on", D));
Put("driver", Bus, ("Topic driver-app-events: send", W)); Put("driver", Blob, ("Driver app's transit container: contributor", W)); Put("driver", Kv, ("The Driver app's endpoint key", D));
Put("portal", Kv, ("The Persona subscription keys", D)); Put("portal", Drv, ("Demo.Fault", R));
Put("sim", Tms, ("TransportOrders.Manage, Estate.Reset", R)); Put("sim", Erp, ("ShipperInvoices.Manage, CarrierInvoices.Manage, Estate.Reset", R)); Put("sim", Drv, ("Estate.Reset", R));
Put("sim", Kv, ("The Persona subscription keys, the Partner portal's reset key, the Shippers' endpoint keys", D));
Put("console", Bus, ("Namespace: list and peek", D), ("Namespace: send, to resubmit", W)); Put("console", Mon, ("Log Analytics Reader", D), ("Logic Apps Standard Reader: opens a run, changes nothing", D));

var specs = new Dictionary<string, GridSpec>
{
    ["identity-access"] = new GridSpec
    {
        Title = "Identity and access · Who may do what",
        Subtitle = "Every caller is a managed identity. Each row holds only the roles its job needs: a Logic App calls one thin operation, reads its own subscription and a few secrets.",
        Footnote = "Source of truth: infra/estate/identities.bicep, the role assignments beside each resource in infra/estate/, and the AppHost's role assignments. Each Logic App's host identity (storage and its content secret) is left out. docs/architecture/identity-and-access.md has the detail.",
        Legend = [new GridLegend("Holds an API role: may call", "role"), new GridLegend("Reads or receives", "read"), new GridLegend("Sends or writes", "write")],
        Columns = columns,
        Groups =
        [
            new GridGroup("Logic Apps: one identity each", "violet", logicApps),
            new GridGroup("Applications and API Management", "blue", apps),
        ],
        Cells = cells.AsReadOnly(),
    },
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "", args, GridDiagram.Render);
