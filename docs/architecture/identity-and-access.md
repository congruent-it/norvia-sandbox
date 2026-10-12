# Identity and access

Who each identity is in Entra, which app roles it holds, which APIs it may call, and which Service Bus subscriptions, containers and secrets it may read. For engineers and coding agents working on the estate and for the technical audience of a demo. Azure is the estate [c4.md](c4.md) draws; what plays Entra locally is in [local-vs-azure.md](local-vs-azure.md#the-map). Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

Everything is declared in [identities.bicep](../../infra/estate/identities.bicep) and the role assignments beside each resource in [infra/estate/](../../infra/estate/). No application holds a client secret: every caller is a managed identity, and the Persona client apps trust the Partner portal's managed identity through federated credentials ([ADR 0005](../adr/0005-partner-identity-comes-from-the-token.md)). There is no drift test for this page; the [coding guidelines §11](../coding-guidelines.md#11-workflow) rule that a PR changing an app role updates the matching architecture page keeps it current.

## The four API audiences

An app role belongs to the app registration of the API it guards. Each registration is an audience; the caller asks Entra for a token for that audience.

| App registration | Audience | Who may get a token | Roles it defines |
|---|---|---|---|
| **Norvia TMS API** | `api://<tenant>/norvia-tms-<env>` | Only principals granted one of its roles (`appRoleAssignmentRequired`) | The [TMS API roles](#tms-api-roles) |
| **Norvia Partner API** | `api://<tenant>/norvia-partner-api-<env>` | Any Persona, holding a role or not, so a Carrier Persona calling a Shipper operation gets 403 from API Management, which the demo shows | The [Partner API roles](#partner-api-roles) |
| **Norvia intake Logic App trigger** | `api://<tenant>/norvia-intake-trigger-<env>` | Only API Management | `TransportOrderIntake.Trigger` |
| **Norvia Carrier-answer Logic App trigger** | `api://<tenant>/norvia-answer-trigger-<env>` | Only API Management | `CarrierAnswer.Trigger` |

Both Logic App triggers sit behind Easy Auth, and the workflow's first step refuses a call that Easy Auth didn't authenticate as API Management ([ADR 0020](../adr/0020-only-api-management-may-call-the-intake-logic-app.md)). The other Logic Apps have no HTTP caller: they read a Service Bus subscription or queue.

## Managed identities

| Identity (`id-…`) | Is | Holds in the TMS API | Other access |
|---|---|---|---|
| `intake` | The intake Logic App (P1) | `TransportOrders.Intake` | |
| `answer` | The Carrier-answer Logic App (P4) | `CarrierBookings.Answer` | |
| `booking` | The Carrier booking Logic App (P4) | `CarrierBookings.Report` | Receives the `carrier-booking` subscription. Reads each Carrier's endpoint key |
| `carrier-cancellation` | The Carrier cancellation Logic App (P7) | `CarrierBookings.Report` | Receives the `carrier-cancellation` subscription. Reads each Carrier's endpoint key |
| `shipper-notification` | The Shipper notification Logic App (P7) | `ShipperNotifications.Report` | Receives the `shipper-notification` subscription. Reads each Shipper's endpoint key |
| `trip-dispatch` | The Trip dispatch Logic App (P11) | `Trips.ReportDelivery` | Receives the `trip-dispatch` subscription. Reads the Driver app's endpoint key |
| `driver-app` | The Driver app's API (P11) | None: it reaches the TMS only through the integration layer | Reads the Driver app's endpoint key |
| `tracking` | The Carrier-tracking Logic App (P5) | `TrackingEvents.Record` | Receives the `carrier-tracking` queue |
| `proof-of-delivery` | The Carrier Proof of delivery Logic App (P6) | `ProofsOfDelivery.Record` | Receives the `carrier-proof-of-delivery` queue. Reads and deletes blobs in the inbound container |
| `tms-ui` | The TMS UI | `TransportOrders.Manage` | |
| `apim` | API Management | `ShipperTracking.Read` | Holds `TransportOrderIntake.Trigger` and `CarrierAnswer.Trigger`. Sends to the `carrier-tracking` and `carrier-proof-of-delivery` queues. Writes blobs in the inbound container. Publishes telemetry to Application Insights |
| `partner-portal` | The Partner portal | | Reads the Persona subscription keys. Each Persona client app trusts it |
| `integration-console` | The Integration console | | Reads the Log Analytics workspace. Reads the run history of all seven Logic Apps |

Each Logic App also has a **host identity** (`id-<name>-host-…`) that only the Logic Apps runtime uses: it owns the Logic App's own storage account and reads the one Key Vault secret holding that account's connection string, which Logic Apps Standard still needs for its content share. Nothing else uses it. The identities are separate on purpose: a Logic App's role in the TMS API is never the same principal as the one that runs its runtime.

Each role below is held by one principal, or by two where the process says so (`CarrierBookings.Report`, and `TrackingEvents.Record` and `ProofsOfDelivery.Record` once the Driver reports Logic App exists). That is the point of ADR 0005: nothing reaches the TMS without passing the integration layer, and a stolen identity can do one thing.

## TMS API roles

The TMS validates the token and the role; the Shipper or Carrier a call is for comes from API Management through the Logic App, never from a payload. The process docs' TMS API tables list the operations per process and link here for the holders.

| Role | Held by | Guards | Process |
|---|---|---|---|
| `TransportOrders.Intake` | `intake` | Submit a Transport Order | [P1](../processes/tms/p1-transport-order-intake.md) |
| `TransportOrders.Manage` | `tms-ui` | What the Planner and Customer service change in the TMS UI: re-rate, assign, release a Shipment, reassign, release without Proof of delivery, withdraw a Carrier booking, and every action on an Exception | [P2](../processes/tms/p2-rating.md), [P3](../processes/tms/p3-transport-planning.md), [P7](../processes/tms/p7-exception-handling.md) |
| `CarrierBookings.Answer` | `answer` | Record a Carrier's confirmation or rejection | [P4](../processes/tms/p4-carrier-booking.md) |
| `CarrierBookings.Report` | `booking`, `carrier-cancellation` | Report a Carrier booking's request, cancellation or withdrawal as sent or not delivered | [P4](../processes/tms/p4-carrier-booking.md), [P7](../processes/tms/p7-exception-handling.md) |
| `ShipperNotifications.Report` | `shipper-notification` | Report a Shipper notification as sent or not delivered | [P7](../processes/tms/p7-exception-handling.md) |
| `Trips.ReportDelivery` | `trip-dispatch` | Report a Trip version as delivered to the Driver app or not delivered | [P11](../processes/tms/p11-trip-dispatch.md#tms-api) |
| `Trips.Start` | None yet: the P12 Driver reports Logic App will hold it | Start a Dispatched Trip for its Driver on the Trip's day | [P12](../processes/tms/p12-trip-execution.md#tms-api) |
| `Trips.End` | None yet: the P12 Driver reports Logic App will hold it | End an In progress Trip for its Driver once every Stop has an outcome | [P12](../processes/tms/p12-trip-execution.md#tms-api) |
| `TrackingEvents.Record` | `tracking`. The P12 Driver reports Logic App will hold it too | Record a Tracking event, a Carrier's or a Driver's Stop report | [P5](../processes/tms/p5-shipment-tracking.md) |
| `ProofsOfDelivery.Record` | `proof-of-delivery`. The P12 Driver reports Logic App will hold it too | Record a Proof of delivery with its files, a Carrier's or a Driver's | [P6](../processes/tms/p6-proof-of-delivery.md) |
| `ShipperTracking.Read` | `apim` | List, get and find a Shipper's Transport Orders, and read a current Proof of delivery file. The TMS trusts the Shipper ID header only from this caller | [P8](../processes/tms/p8-shipper-visibility.md) |

The TMS API's queries (Transport Orders, Shipments, Exceptions, Proof of delivery files) ask only for a valid token. Because Entra issues one only to a principal holding one of the roles above, in practice the callers are these identities. The TMS UI reads in process and uses the API to change things ([ADR 0028](../adr/0028-the-tms-ui-reads-in-process-and-changes-things-through-the-tms-api.md)).

Planned and not yet in Entra: `Trips.Start`, `Trips.End`, with `TrackingEvents.Record` and `ProofsOfDelivery.Record`, for the Driver reports Logic App ([P12](../processes/tms/p12-trip-execution.md#tms-api)).

## Partner API roles

API Management validates the Persona's token for each operation, takes the Shipper or Carrier from the token's `azp` claim, and passes it on. A Persona's client app holds only the roles its party needs.

| Role | Held by | Guards (Norvia Partner API) | Process |
|---|---|---|---|
| `TransportOrders.Submit` | Shipper Personas | Book a Transport Order | [P1](../processes/tms/p1-transport-order-intake.md) |
| `Tracking.Read` | Shipper Personas | Read a Shipper's Transport Orders with their Tracking status, and Proof of delivery files | [P8](../processes/tms/p8-shipper-visibility.md) |
| `CarrierBookings.Respond` | Carrier Personas | Confirm or reject a Carrier booking | [P4](../processes/tms/p4-carrier-booking.md) |
| `Tracking.Submit` | Carrier Personas | Report a Tracking event | [P5](../processes/tms/p5-shipment-tracking.md) |
| `ProofsOfDelivery.Submit` | Carrier Personas | Submit a Proof of delivery with its files | [P6](../processes/tms/p6-proof-of-delivery.md) |

The Personas come from [norvia.seed.json](../../seed/norvia.seed.json); each is a client app registration (`norvia-persona-<id>`) with no secret.

| Persona | Party | Roles |
|---|---|---|
| Brouwer Dranken | Shipper SHP-0001 | `TransportOrders.Submit`, `Tracking.Read` |
| Tuincentrum Groenrijk | Shipper SHP-0003 | `TransportOrders.Submit`, `Tracking.Read` |
| Van Dijk Transport | Carrier CAR-0001 | `CarrierBookings.Respond`, `Tracking.Submit`, `ProofsOfDelivery.Submit` |
| Kessler Spedition | Carrier CAR-0002 | as Van Dijk Transport |
| PakketPunt Express | Carrier CAR-0003 | as Van Dijk Transport |

A Persona also needs its own subscription key in API Management, which the Partner portal reads from its Key Vault. The key picks the product and the rate limit; the token supplies the identity.

## People

Users are assigned app roles on the enterprise application, not on an API. Entra refuses a user without a role before the app runs (`appRoleAssignmentRequired`).

| App registration | Role | Lets the user | Held by |
|---|---|---|---|
| **Norvia demo UIs** | `DemoUis.Use` | Open the TMS UI and the Partner portal in Azure ([ADR 0024](../adr/0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)). The apps read no roles and don't know who signed in | The presenter, who ran `azd up` |
| **Norvia Integration console** | `IntegrationConsole.Read` | Search Message trails and open run details and message bodies. The console refuses a user without it ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)) | The presenter, who plays the Integration operator |
| | `IntegrationConsole.Resubmit` | Also resubmit and discard dead-lettered messages ([planned](../operations/integration-console.md#resubmit-and-discard)) | The presenter |

Someone else presenting or operating needs the role assigned in Entra ID. The presenter also holds two Azure RBAC roles for looking behind the scenes, both optional (`presenterPrincipalId` empty for none): **Azure Service Bus Data Receiver** on the namespace, to read messages in the Service Bus Explorer, and **Storage Blob Data Reader** on the inbound container.

## What each identity may read

Azure RBAC, scoped as narrowly as the resource allows, so an identity sees its own messages and nothing else.

| Resource | Scope | Identity | Azure role |
|---|---|---|---|
| Service Bus subscription `carrier-booking` | The subscription | `booking` | Data Receiver |
| Service Bus subscription `shipper-notification` | The subscription | `shipper-notification` | Data Receiver |
| Service Bus subscription `carrier-cancellation` | The subscription | `carrier-cancellation` | Data Receiver |
| Service Bus subscription `trip-dispatch` | The subscription | `trip-dispatch` | Data Receiver |
| Service Bus queue `carrier-tracking` | The queue | `apim` | Data Sender |
| | | `tracking` | Data Receiver |
| Service Bus queue `carrier-proof-of-delivery` | The queue | `apim` | Data Sender |
| | | `proof-of-delivery` | Data Receiver |
| Inbound container for Proof of delivery files | The container | `apim`, `proof-of-delivery` | Storage Blob Data Contributor |
| Partner portal's Key Vault | The vault | `partner-portal` | Key Vault Secrets User |
| Each Carrier endpoint key | The secret | `booking`, `carrier-cancellation` | Key Vault Secrets User |
| Each Shipper endpoint key | The secret | `shipper-notification` | Key Vault Secrets User |
| The Driver app's endpoint key (`driver-app-endpoint-key`, in the Driver app's Key Vault) | The secret | `driver-app`, `trip-dispatch` | Key Vault Secrets User |
| Logic App's content connection string | The secret | The Logic App's host identity | Key Vault Secrets User |
| Logic App's storage account | The account | The Logic App's host identity | Blob Data Owner, Queue and Table Data Contributor, Account Contributor |
| Log Analytics workspace | The workspace | `integration-console` | Log Analytics Reader |
| Each Logic App | The app | `integration-console` | Logic Apps Standard Reader, so it opens a run and changes nothing |
| Application Insights | The component | `apim` | Monitoring Metrics Publisher |

The scoping is the design: the booking Logic App cannot read a Persona's subscription key or a Shipper's credential, API Management cannot read the queue it sends to, and the Integration console cannot change a Logic App. The Integration console calls Azure as its own identity and never with the operator's token ([ADR 0017](../adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)).

## Locally

There is no Entra under Aspire. Every caller still asks for a token with its own client ID and roles, signed with one key, and the TMS API trusts that issuer. A role a caller gets locally is therefore no proof Entra grants it: the tables above are the truth. The Integration console signs the developer in as a local operator holding both console roles ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)).

## Changing an identity

A PR that adds or changes an app role, a role assignment or an identity updates this page, the process doc's TMS API table and the [C4 identity diagram](c4.md#identity-secrets-and-telemetry) if a container's access changes, in the same PR. Give a new caller its own managed identity and the one role it needs; don't add a role to an existing identity because it already works.
