# Locally, the Integration console searches the Aspire dashboard's telemetry

The Integration console searches Message trails in the Log Analytics workspace behind the estate's Application Insights ([ADR 0017](0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). Locally no app sends telemetry there: `ServiceDefaults` exports to Application Insights only when the AppHost gives an app a connection string, which it does only in Azure. Local traces go over OTLP to the Aspire dashboard. So a booking a developer made in the local Partner portal could not be found in the console.

Locally, then, the console searches the telemetry the Aspire dashboard holds, through the dashboard's telemetry HTTP API. The dashboard of Aspire 13.5, the version in `Directory.Packages.props`, serves `GET /api/telemetry/logs`, `/spans`, `/traces` and `/resources` in OTLP JSON. The API is on by default, and it is secured with an API key sent in the `x-api-key` header ([Aspire dashboard APIs](https://aspire.dev/dashboard/apis/), [configuration](https://aspire.dev/dashboard/configuration/#api)). The AppHost generates that key as `AppHost:DashboardApiKey` and keeps it in its user secrets. The dashboard listens on the AppHost launch profile's `ASPNETCORE_URLS`. The AppHost passes both to the console as `LocalTelemetry` settings, and the console then searches with `DashboardTrailSource` instead of `LogAnalyticsTrailSource`:

| Where the console runs | Where it searches |
|---|---|
| In Azure | The estate's Log Analytics workspace |
| Locally, with a deployed estate's workspace given as user secrets | That workspace |
| Locally, without one | The Aspire dashboard |

The adapter answers the same search as the Log Analytics one. Locally every app puts the tracking contract's keys in its logging scope (coding guidelines §7), and they reach the dashboard as log record attributes. The adapter reads every log record the dashboard holds, keeps each one's time and tracking keys, and never reads a record's body. It then finds the trails that carried the key, as the KQL does, and the Application layer caps the range and orders the results as before. To open one trail, it also asks `/api/telemetry/traces/{traceId}` for the spans of each trace the trail's records were written in: a hop is the request or received message a record was written in, its duration is that span's, and the hop that caused it is the nearest hop up the span's parents. A record written outside any span, or in a trace the dashboard no longer holds, is a hop of its own at its own time. The local Partner API stands in for API Management and the intake Logic App ([ADR 0023](0023-locally-a-stand-in-plays-the-norvia-partner-api.md)). So it records the keys they record: a refusal's `outcome` and `failureCode`, as the policy's traces do, and the TMS call's `outcome`, `failureCode` and `transportOrderNumber`, as the workflow's tracked properties do.

The same dashboard feeds the Failed runs page locally. There are no Logic Apps to read run history from, so `DashboardTrailSource` also lists as failed runs the records whose `outcome` is `Failed`, one per correlation ID, flow and application, as of the last. Such a row has its flow, time, failure code and correlation ID, which opens the trail, but no failing step and no run to open: the page leaves those out. In Azure, and when a developer points the console at a deployed estate, failed runs come from the Logic Apps management API as before.

## Considered Options

- **The dashboard's MCP server, or the AppHost's resource service:** the MCP server is built for AI agents, not for an application to query. The resource service is the gRPC API the AppHost serves the dashboard, for resources and console logs, not telemetry.
- **The console collecting OTLP itself, alongside the dashboard:** needs no dashboard API. But the AppHost would have to send every app's telemetry to two places, and the console would keep what it received, against ADR 0017's "stores nothing".
- **An opt-in that sends local telemetry to a deployed Application Insights:** one code path everywhere, but local and Azure trails mix. The local TMS and the deployed one each number Transport Orders from their own database, so `TO-0000001` would find two unrelated trails. Search would also lag by Application Insights' ingestion delay, and it would need an estate even for local work.
- **Filtering in the dashboard with its `search` parameter:** less data over the wire. But its qualifiers are ANDed `contains` matches, so they can't say "any tracking key equals this one", and `TO-1` would match `TO-10`.

## Consequences

- Locally the console finds only what the dashboard still holds: the current AppHost run, and at most its `Dashboard:TelemetryLimits:MaxLogCount` records (10,000 by default). Restarting the AppHost empties it. The Application layer still caps the range at the placeholder retention the AppHost gives the console locally.
- The telemetry API has no way to ask for attributes only, so each search downloads every log record the dashboard holds, formatted messages included, and the adapter drops all but the time and the tracking keys as it parses them. It all stays on the developer's machine, and nothing is shown or kept.
- Search lags by the OTLP exporter's batch interval, a few seconds, rather than Application Insights' minutes.
- No local telemetry reaches the estate. The AppHost gives no local app an Application Insights connection string, and an AppHost model test checks it.
- The console depends on an HTTP API that Aspire documents but doesn't version. Its routes and OTLP JSON shape are pinned by `DashboardTrailSourceTests`, and an Aspire upgrade should be checked against a local booking.
- Under `Aspire.Hosting.Testing` the dashboard is off, so the AppHost gives the console no dashboard, and search says it can't be run.
