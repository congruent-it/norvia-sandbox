#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// How people and the Simulator sign in, as three sequence diagrams.
//
//     dotnet run docs/architecture/diagrams/sign-in.cs -- [name ...]    # writes sign-in-<name>.excalidraw and .png; no names draws all
//
// Sequence style (see README.md). The PNGs need rsvg-convert.

using Norvia.Docs.Diagrams;

static LegendItem L(string label, string key) => Palette.Legend(label, key);
static SequenceParticipant P(string id, string title, string pal = "violet", string sub = "", string? kind = null) => new(id, title, pal, sub, kind);
const string LegendArrows = "Solid arrows are calls, dashed arrows are replies. No sign-in holds a client secret.";
var Easy = P("easy", "Easy Auth", "gray", "Container Apps authentication");

var specs = new Dictionary<string, SequenceSpec>();

specs["easy-auth"] = new SequenceSpec
{
    Title = "Sign-in 1 · A person signs in to a UI with Easy Auth",
    Subtitle = "Shown for the Integration console. The demo UIs, the ERP UI and the Simulator sign in the same way and differ only in the role they check.",
    Footnote = LegendArrows + " Source of truth: ADR 0024 and 0026, and docs/architecture/identity-and-access.md.",
    Legend = [Palette.LegendPerson("A person"), L("Outside Norvia", "gray"), L("Integration console", "pink")],
    Participants = [
        P("operator", "Integration operator", "gray", "the presenter, in a demo", "person"),
        Easy,
        P("entra", "Microsoft Entra ID", "gray", "Norvia Integration console registration"),
        P("console", "Integration console", "pink", "Container App"),
        P("azure", "Azure", "gray", "Log Analytics, Service Bus, ARM"),
    ],
    Steps = [
        new Phase("Signing in"),
        new Message("operator", "easy", "Opens the console's address"),
        new SelfStep("easy", "No session: sends the browser to Entra and asks for an ID token only, since it holds no client secret"),
        new Message("easy", "entra", "Sign-in request"),
        new SelfStep("entra", "Refuses a user with no role on the registration (appRoleAssignmentRequired), signed in to the tenant or not"),
        new Message("entra", "easy", "ID token with the user's app roles, to the registered callback", Dashed: true),
        new SelfStep("easy", "Starts a session"),
        new Phase("Using it"),
        new Message("operator", "easy", "Opens a page"),
        new Message("easy", "console", "The request, with the user and roles in X-MS-CLIENT-PRINCIPAL. Container Apps strips that header from a request from outside"),
        new SelfStep("console", "Every page needs IntegrationConsole.Read. A user without it sees which role is missing"),
        new Message("console", "azure", "Reads telemetry, queues and runs as its own managed identity, never with the operator's token"),
        new Message("console", "operator", "The page", Dashed: true),
        new Phase("Locally"),
        new SelfStep("console", "No Easy Auth: the AppHost signs the developer in as a local operator with both roles. It refuses those settings outside Development"),
    ],
};


specs["driver"] = new SequenceSpec
{
    Title = "Sign-in 2 · A Driver signs in to the Driver app",
    Subtitle = "The Driver ID is the Driver's app role in the token, so the Driver app never takes it from a request.",
    Footnote = LegendArrows + " Source of truth: ADR 0037 and docs/architecture/identity-and-access.md.",
    Legend = [Palette.LegendPerson("A person"), L("Outside Norvia", "gray"), L("Driver app", "teal"), L("Integration layer", "violet")],
    Participants = [
        P("driver", "Driver", "teal", "Norvia Entra test user", "person"),
        Easy,
        P("entra", "Microsoft Entra ID", "gray", "Norvia Driver app registration"),
        P("app", "Driver app", "teal", "Container App"),
        P("bus", "Service Bus", "violet", "driver-app-events"),
    ],
    Steps = [
        new Phase("Signing in"),
        new Message("driver", "easy", "Opens the Driver app"),
        new Message("easy", "entra", "Sign-in request, asking for an ID token only"),
        new SelfStep("entra", "Refuses a user with no role. A Driver holds exactly one role, with their Driver ID as its value (DRV-0001)"),
        new Message("entra", "easy", "ID token carrying that one role", Dashed: true),
        new Phase("Using it"),
        new Message("easy", "app", "The request, with the user and roles in X-MS-CLIENT-PRINCIPAL"),
        new SelfStep("app", "Turns the role into the driverId claim. Every screen needs it: a user without it sees a page that says so"),
        new SelfStep("app", "Shows only that Driver's Trips and takes their reports, with the Driver ID from the token and never from the request"),
        new Message("app", "driver", "Their Trips", Dashed: true),
        new Message("app", "bus", "Each report, with the Driver ID, through the Driver app's outbox", Dashed: true),
        new Phase("Locally"),
        new SelfStep("app", "No Entra: a sign-in page lists the seeded Drivers and signs the chosen one in with a cookie holding the same driverId claim. Development only"),
    ],
};


specs["simulator"] = new SequenceSpec
{
    Title = "Sign-in 3 · The Simulator reaches the estate",
    Subtitle = "One managed identity, and a client app for each Persona and each Driver that trusts it. The estate decides what each may do.",
    Footnote = LegendArrows + " Source of truth: ADR 0042, ADR 0005 and ADR 0037.",
    Legend = [Palette.LegendPerson("A person"), L("Outside Norvia", "gray"), L("Simulator", "orange"), L("Integration layer", "violet"), L("TMS", "blue"), L("Driver app", "teal")],
    Participants = [
        P("presenter", "Presenter", "gray", "signed in with Easy Auth", "person"),
        P("sim", "Simulator", "orange", "managed identity"),
        P("entra", "Microsoft Entra ID", "gray", "client apps, federated credentials"),
        P("kv", "Partner portal's Key Vault", "gray", "subscription keys"),
        P("apim", "API Management", "violet", "Norvia Partner API"),
        P("tms", "TMS", "blue", "TMS API"),
        P("app", "Driver app", "teal", "Driver app API"),
    ],
    Steps = [
        new Phase("The presenter"),
        new Message("presenter", "sim", "Starts a run. Needs Simulator.Run, checked as in sign-in 1"),
        new Phase("As a Shipper or a Carrier Persona"),
        new Message("sim", "entra", "Asks for a token for the Persona's client app, which trusts the Simulator's identity"),
        new Message("entra", "sim", "Token with the roles the Persona already holds", Dashed: true),
        new Message("sim", "kv", "Reads the Persona's subscription key"),
        new Message("sim", "apim", "Calls the Norvia Partner API with the token and the key"),
        new SelfStep("apim", "Takes the party from the token's azp, as it does for the Partner portal"),
        new Phase("As the Planner"),
        new Message("sim", "entra", "Asks for a token with its own identity (TransportOrders.Manage)"),
        new Message("sim", "tms", "Assignments, releases, Trips and Exceptions, as the TMS UI's"),
        new Phase("As a Driver"),
        new Message("sim", "entra", "Asks for a token for that Driver's client app"),
        new Message("entra", "sim", "Token with one app role: the Driver ID", Dashed: true),
        new Message("sim", "app", "The Driver's actions on /v1/driver/trips"),
        new SelfStep("app", "Takes the Driver ID from that one role, as it does from a Driver's own sign-in"),
    ],
};

DiagramOutput.RenderAll(specs, DiagramOutput.SpecDirectory(), "sign-in-", args, SequenceDiagram.Render);
