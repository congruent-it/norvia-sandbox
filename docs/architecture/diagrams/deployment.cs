#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The deployment view and the local-versus-Azure map.
//
//     dotnet run docs/architecture/diagrams/deployment.cs -- [name ...]    # writes <name>.excalidraw and .png; no names draws all
//
// Graph style (see README.md). The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

static LegendItem L(string label, string key) => Palette.Legend(label, key);
var LegPerson = Palette.LegendPerson();
var LegPlanned = Palette.LegendPlanned();
const string V = "violet";
const string Cl = "gray", Vi = "violet", Bl = "blue";
var specs = new Dictionary<string, GraphSpec>();

specs["deployment"] = new GraphSpec
{
    Title = "Deployment view · The estate in Azure",
    Subtitle = "One resource group per environment. The estate Bicep owns the integration layer and what it leans on, and the Aspire AppHost owns the applications, Azure SQL and the TMS's storage (ADR 0004).",
    Footnote = "Source of truth: infra/estate.bicep and infra/estate/, src/Norvia.AppHost/AppHost.cs and docs/adr (0004, 0021, 0024). azd deploys the estate first, then Aspire provisions the apps into the same resource group.",
    Legend = [L("Integration layer", V), L("TMS", "blue"), L("ERP", "green"), L("Driver app", "teal"), L("Partner portal", "gray"), L("Simulator", "orange"), L("Integration console", "pink"), Palette.LegendPerson("A person")],
    Cell = (390, 120),
    Nodes = [
        new GraphNode { Id = "person", Col = 0, Row = 2.8, Kind = "person", W = 240, Title = "Presenter, Integration operator", Sub = "Open the UIs through the Entra gate" },
        new GraphNode { Id = "entra", Col = 0, Row = 4.6, Palette = "gray", W = 240, Title = "Microsoft Entra ID", Sub = "App registrations and app roles, a client app per Persona, Easy Auth in front of the UIs. Tenant-wide, not in the resource group" },
        new GraphNode { Id = "portal", Col = 1.3, Row = 0.4, Palette = "gray", W = 250, Title = "Partner portal", Sub = "[Container App]" },
        new GraphNode { Id = "tms", Col = 1.3, Row = 1.5, Palette = "blue", W = 250, Title = "TMS API and UI", Sub = "[Container Apps]" },
        new GraphNode { Id = "erp", Col = 1.3, Row = 2.6, Palette = "green", W = 250, Title = "ERP API and UI", Sub = "[Container Apps]" },
        new GraphNode { Id = "driver", Col = 1.3, Row = 3.7, Palette = "teal", W = 250, Title = "Driver app API and UI", Sub = "[Container Apps]" },
        new GraphNode { Id = "sim", Col = 1.3, Row = 4.8, Palette = "orange", W = 250, Title = "Simulator", Sub = "[Container App] a demo tool, can be switched off" },
        new GraphNode { Id = "console", Col = 1.3, Row = 5.9, Palette = "pink", W = 250, Title = "Integration console", Sub = "[Container App]" },
        new GraphNode { Id = "jobs", Col = 1.3, Row = 7.0, Palette = "lightgray", W = 250, Title = "Migration jobs ×5", Sub = "[Container Apps jobs] One per database, run by azd's postdeploy hook" },
        new GraphNode { Id = "apim", Col = 2.9, Row = 0.4, Palette = V, W = 290, Title = "API Management", Sub = "[Developer tier, one unit] The Norvia Partner API and its policies. It can't be stopped, only deleted (ADR 0021)" },
        new GraphNode { Id = "las", Col = 2.9, Row = 1.6, Palette = V, W = 290, Title = "14 Logic Apps", Sub = "[Logic Apps Standard] One Flow each, each with its own WS1 plan and storage account" },
        new GraphNode { Id = "bus", Col = 2.9, Row = 2.9, Palette = V, W = 290, Title = "Service Bus namespace", Sub = "[Standard] Five topics with filtered subscriptions, and the carrier-tracking and carrier-proof-of-delivery queues" },
        new GraphNode { Id = "inbound", Col = 2.9, Row = 4.1, Palette = V, W = 290, Title = "Inbound storage", Sub = "[Storage account] The claim-check container for Carrier Proofs of delivery" },
        new GraphNode { Id = "kvs", Col = 2.9, Row = 5.2, Palette = V, W = 290, Title = "Key Vaults", Sub = "The Logic Apps' content secret, the Partner portal's keys, the Driver app's endpoint key" },
        new GraphNode { Id = "mon", Col = 2.9, Row = 6.4, Palette = V, W = 290, Title = "Log Analytics and Application Insights", Sub = "Every trace unsampled, kept for integrationRetentionDays" },
        new GraphNode { Id = "ids", Col = 2.9, Row = 7.5, Palette = V, W = 290, Title = "Managed identities", Sub = "One per caller, holding only the app roles it needs" },
        new GraphNode { Id = "sql", Col = 4.6, Row = 1.2, Palette = "blue", W = 270, Title = "Azure SQL", Sub = "[Serverless] One server, five databases: tms, erp, driver-app, partner-portal, simulator" },
        new GraphNode { Id = "store", Col = 4.6, Row = 2.9, Palette = "blue", W = 270, Title = "Storage account", Sub = "[Blob] The TMS's Proof of delivery files, cooled after 30 days" },
    ],
    Groups = [
        new GraphGroup { Id = "env", Title = "Owned by the Aspire AppHost: Container Apps environment 'apps'", Palette = "orange", Members = ["portal", "tms", "erp", "driver", "sim", "console", "jobs"] },
        new GraphGroup { Id = "bicep", Title = "Owned by the estate Bicep", Palette = V, Members = ["apim", "las", "bus", "inbound", "kvs", "mon", "ids"] },
        new GraphGroup { Id = "data", Title = "Owned by the Aspire AppHost: data", Palette = "blue", Members = ["sql", "store"] },
        new GraphGroup { Id = "rg", Title = "Resource group rg-<environment>", Palette = "gray", Members = ["env", "bicep", "data"] },
    ],
    Edges = [
        new GraphEdge("person", "entra", "sign in"),
        new GraphEdge("person", "tms", "browser, through the gate"),
        new GraphEdge("portal", "apim", "HTTPS, the Persona's token and key"),
        new GraphEdge("apim", "las", "HTTPS, managed identity"),
        new GraphEdge("apim", "inbound", "claim-check file"),
        new GraphEdge("apim", "bus", "queues"),
        new GraphEdge("apim", "tms", "Shipper queries"),
        new GraphEdge("las", "tms", "commands and reports"),
        new GraphEdge("las", "erp", "ERP billing API"),
        new GraphEdge("las", "driver", "Trip endpoint"),
        new GraphEdge("las", "portal", "Persona endpoints"),
        new GraphEdge("tms", "bus", "events, through the outbox"),
        new GraphEdge("erp", "bus", "events"),
        new GraphEdge("driver", "bus", "events"),
        new GraphEdge("console", "bus", "dead-letter queues"),
        new GraphEdge("tms", "sql", "each application its own database"),
        new GraphEdge("jobs", "sql", "migrations"),
        new GraphEdge("tms", "store", "Proof of delivery files"),
        new GraphEdge("las", "mon", "traces"),
        new GraphEdge("console", "mon", "Message trails"),
        new GraphEdge("las", "kvs", "secrets"),
    ],
};


// ------------------------------------------------------------------ local versus Azure
const string Az = "gray", Loc = "orange";
(string Azure, string AzureSub, string Local, string LocalSub, string Adr)[] rows =
[
    ("Norvia Partner API", "[API Management policies]", "LocalPartnerApi", "[ASP.NET Core app] The same routes and checks, in the same order", "0023"),
    ("Intake and Carrier answer Logic Apps", "[P1, P4]", "LocalPartnerApi, in the request", "Calls the same maps and relay classes the Logic Apps' local functions wrap", "0019"),
    ("Carrier tracking and Proof of delivery Logic Apps", "[P5, P6]", "LocalTrackingWorker, LocalProofOfDeliveryWorker", "[hosted workers] LocalPartnerApi accepts and queues", "0031, 0032"),
    ("Booking, Shipper notification and cancellation Logic Apps", "[P4, P7]", "LocalBookingWorker, LocalShipperNotificationWorker, LocalCarrierCancellationWorker", "[hosted workers]", "0030, 0033, 0034"),
    ("Trip dispatch and Driver reports Logic Apps", "[P11, P12]", "LocalTripDispatchWorker, LocalDriverReportsWorker", "[hosted workers] The reports worker reads the session-enabled subscription", "0036, 0038"),
    ("Billing, Carrier cost, Shipper invoice and Carrier invoice Logic Apps", "[E1, E2, P9]", "LocalBillingWorker, LocalCarrierCostWorker, LocalShipperInvoiceWorker, LocalCarrierInvoiceWorker, LocalCarrierInvoiceOutcomeWorker", "[hosted workers]", "0043, 0045, 0046"),
    ("Service Bus", "[topics, subscriptions, queues]", "Two Service Bus emulators", "messaging and messaging-2: each accepts only 10 concurrent connections", "0004"),
    ("Inbound storage and the TMS's Proof of delivery files", "[Blob storage]", "Azurite", "With the inbound-proofs-of-delivery container and the TMS's own", "0032"),
    ("Azure SQL", "[five databases]", "SQL Server in a container", "With DbGate beside it. Migrations run as a project the apps wait for", "0004"),
    ("Entra tokens and app roles", "[a client app per Persona, a managed identity per caller]", "Tokens signed with one local key", "The TMS API trusts that issuer. Each caller still asks for a token with its own roles", "0005"),
    ("Entra sign-in in front of the UIs and the Driver app", "[Easy Auth]", "A local operator, and a Driver sign-in page", "Refused outside Development", "0024, 0026, 0037"),
    ("Key Vault", "[Persona keys, endpoint credentials]", "Keys the AppHost generates for the run", "Passed to both sides, from the Personas in the seed file", "0023"),
    ("Application Insights and Log Analytics", "[every trace, unsampled]", "The Aspire dashboard", "Over OTLP. The console searches it with DashboardTrailSource", "0027"),
    ("Logic Apps management API", "[opening a run from the console]", "Nothing", "There are no runs to open, only the workers' traces", "0027"),
];
List<GraphNode> nodes = [];
List<GraphEdge> edges = [];
for (var i = 0; i < rows.Length; i++)
{
    var (at, asub, lt, lsub, adr) = rows[i];
    nodes.Add(new GraphNode { Id = $"a{i}", Col = 0, Row = i * 0.95, Palette = Az, W = 330, Title = at, Sub = asub });
    nodes.Add(new GraphNode { Id = $"l{i}", Col = 1.25, Row = i * 0.95, Palette = Loc, W = 400, Title = lt, Sub = lsub });
    edges.Add(new GraphEdge($"a{i}", $"l{i}", $"ADR {adr}"));
}

specs["local-vs-azure"] = new GraphSpec
{
    Title = "Local versus Azure · What plays each Azure piece",
    Subtitle = "Under Aspire there is no subscription, no API Management, no Logic Apps runtime and no Entra. Stand-ins run the same code, so a booking leaves the same Message trail.",
    Footnote = "Source of truth: docs/architecture/local-vs-azure.md and the ADRs it cites. The applications themselves are the same code in both places and need no row.",
    Legend = [L("In Azure", Az), L("Under Aspire, on a developer's machine", Loc)],
    Cell = (420, 118),
    Nodes = nodes,
    Groups = [],
    Edges = edges,
};


// ------------------------------------------------------------------ the sandbox on Kubernetes
specs["kubernetes"] = new GraphSpec
{
    Title = "Kubernetes · The local topology as pods",
    Subtitle = "One namespace, norvia, on a shared cluster. The chart installs no ingress controller, issuer or storage class, and nothing in it reaches Azure (ADR 0053).",
    Footnote = "Source of truth: deploy/helm/norvia/ (values.yaml and templates) and docs/adr/0053-kubernetes-runs-the-local-topology.md.",
    Legend = [L("Owned by the cluster", Cl), L("Integration layer and applications, as under Aspire", Vi), L("Data", Bl), L("Telemetry and keys", "teal")],
    Cell = (390, 120),
    Nodes = [
        new GraphNode { Id = "person", Col = 0, Row = 0.4, Kind = "person", W = 240, Title = "Operator", Sub = "helm upgrade --install, the smoke script, a browser on the LAN or VPN" },
        new GraphNode { Id = "ingress", Col = 0, Row = 1.8, Palette = Cl, W = 240, Title = "Ingress controller", Sub = "[Traefik on K3S] An Ingress per application at <host>.<baseDomain>" },
        new GraphNode { Id = "issuer", Col = 0, Row = 3.1, Palette = Cl, W = 240, Title = "cert-manager issuer", Sub = "[edge.tls.clusterIssuer] Certificate into <application>-tls" },
        new GraphNode { Id = "storage", Col = 0, Row = 4.4, Palette = Cl, W = 240, Title = "Storage class", Sub = "[persistence.storageClass] Empty: the cluster default" },
        new GraphNode { Id = "api", Col = 1.3, Row = 0.4, Palette = Vi, W = 270, Title = "partner-api", Sub = "[Deployment, 1 replica] LocalPartnerApi: plays API Management and the intake and answer Logic Apps" },
        new GraphNode { Id = "mcp", Col = 1.3, Row = 1.5, Palette = Vi, W = 270, Title = "agent-mcp", Sub = "[Deployment] Only when agents.enabled. The Agents are scripted fakes" },
        new GraphNode { Id = "workers", Col = 1.3, Row = 2.6, Palette = Vi, W = 270, Title = "13 Local*Worker", Sub = "[Deployments, 1 replica each] One per Logic App. carrier-invoice-worker alone has a Service and probes" },
        new GraphNode { Id = "dash", Col = 1.3, Row = 3.8, Palette = "teal", W = 270, Title = "aspire-dashboard", Sub = "[Deployment] OTLP on 18889, in memory. The UI has no Ingress: kubectl port-forward" },
        new GraphNode { Id = "apps", Col = 1.3, Row = 5.0, Palette = Vi, W = 270, Title = "The applications", Sub = "[Deployments, an Ingress each] TMS, ERP, Driver app, Integration console, Estate health check, Partner portal, Simulator. Those with a database wait for its migrations" },
        new GraphNode { Id = "bus1", Col = 2.9, Row = 2.0, Palette = Vi, W = 290, Title = "messaging", Sub = "[Deployment, Service, ConfigMap] Service Bus emulator, 10 connections" },
        new GraphNode { Id = "bus2", Col = 2.9, Row = 0.4, Palette = Vi, W = 290, Title = "messaging-2", Sub = "[Deployment, Service, ConfigMap] The Driver app, ERP and Carrier tracking path topics and queues" },
        new GraphNode { Id = "sql", Col = 2.9, Row = 3.3, Palette = Bl, W = 290, Title = "sql", Sub = "[StatefulSet, PVC on the storage class] Five databases, and the emulators' state. No backup, no HA" },
        new GraphNode { Id = "azurite", Col = 2.9, Row = 5.5, Palette = Bl, W = 290, Title = "azurite", Sub = "[StatefulSet, PVC on the storage class] Blob service only, the claim-check files" },
        new GraphNode { Id = "jobs", Col = 2.9, Row = 4.4, Palette = Bl, W = 290, Title = "Migration Jobs ×5", Sub = "One per database, named with the release revision. Applications wait for them in an init container" },
        new GraphNode { Id = "secrets", Col = 2.9, Row = 6.6, Palette = "teal", W = 290, Title = "Secrets", Sub = "local-tokens, subscription-keys, endpoint-keys, the SA password, the dashboard key. Generated at install, kept on upgrade" },
    ],
    Groups = [new GraphGroup { Id = "ns", Title = "Namespace norvia", Palette = "orange", Members = ["api", "mcp", "workers", "dash", "apps", "bus1", "bus2", "sql", "azurite", "jobs", "secrets"] }],
    Edges = [
        new GraphEdge("person", "ingress", "HTTPS"),
        new GraphEdge("issuer", "ingress", "certificates"),
        new GraphEdge("ingress", "api", "partner-api host"),
        new GraphEdge("api", "bus2", "queues"),
        new GraphEdge("workers", "bus1", "reads"),
        new GraphEdge("jobs", "sql", "migrations"),
    ],
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "", args, GraphDiagram.Render);
