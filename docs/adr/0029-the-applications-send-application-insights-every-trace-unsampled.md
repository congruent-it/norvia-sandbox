# The applications send Application Insights every trace, unsampled

The Integration console finds a Message trail only from the records that reach Application Insights ([ADR 0017](0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). `ServiceDefaults` sends the applications' telemetry there with `UseAzureMonitor()`. Since version 1.5.0, `Azure.Monitor.OpenTelemetry.AspNetCore` samples by default, at 5 traces per second, and the logs of a sampled-out trace are dropped with it. The TMS's own background SQL (the outbox and the rating sweep) uses up most of that budget. So in the presenter smoke run for Rating on the `dev` estate, the rating worker's logs and trace were lost for two Transport Orders, and one order's `Accepted` log too (#69). Every flow's trail was incomplete at random, not only Rating's.

So `ServiceDefaults` turns sampling off: `SamplingRatio` 1 and no `TracesPerSecond`. Every record carrying the [tracking contract](../coding-guidelines.md#tracking-contract)'s keys then reaches Application Insights, whatever the load. The rest of the estate already keeps its tracked records. API Management's diagnostics log every call (`percentage: 100` in `infra/estate/partner-api.bicep`). A Logic App writes its trigger and action events, which carry the tracked properties, to the Requests table, and the intake Logic App's `host.json` leaves requests and exceptions out of sampling.

## Considered Options

- **Keep sampling, but keep the background polling spans out of the budget and the tracked records in it:** less ingestion. But it needs a custom sampler, or filters on each poller's spans, that every new background job must remember. A record the filter misses is again lost at random, and nothing shows it.

## Consequences

- Ingestion grows with the background polling, which sends a span for every poll. That costs more, but little at the sandbox's volume. If it grows, stop the pollers' spans at their source, not by sampling.
- A new application gets unsampled telemetry through `ServiceDefaults`.
- A new Logic App's `host.json` must also leave `Request` and `Exception` out of its sampling, as the intake Logic App does.
- The retention of what arrives is still `integrationRetentionDays`.
