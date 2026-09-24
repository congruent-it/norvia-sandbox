# The Integration console reads the estate's telemetry and stores nothing

The Integration operator needs one place to follow a message through the integration layer and to resolve the ones that failed. We build that as its own application, the Integration console, and it keeps no copy of any message. It searches Application Insights, where every flow writes the keys of the [tracking contract](../coding-guidelines.md#tracking-contract). It reads run steps and their bodies live from the Logic Apps management API, and dead-lettered messages live from Service Bus. Its only writes are resubmitting and discarding dead-lettered messages, audited as events in Application Insights. So the console shows exactly what the estate remembers, for as long as the estate's retention allows, and no longer.

Resubmitting a subscription's dead-lettered message goes back through its topic with a `resubmitTo` property naming the subscription. Every subscription's filter accepts its own name and ignores the others, because Service Bus can't send to a single subscription, and resending to the topic unmarked would start every other subscriber again. The console never resubmits a Logic App run: a request-response flow has already told its partner the call failed, and a queued flow's durable record is its message.

## Considered Options

- **The Azure portal alone** (run history, Service Bus Explorer, Application Insights): nothing to build, but a failure's trail is spread over four blades, and a non-technical demo audience can't follow it.
- **An Azure Monitor workbook in the estate Bicep:** covers search and counts with no code, but can't resubmit or discard well.
- **A message archive** (every body copied to a store of the console's own): trails that outlive retention, but Consignee personal data in a second store, and a cost that grows with volume. Norvia's lasting record is the TMS and the ERP.
- **Search from live sources only:** no ingestion delay, but no search by business key, because run history can't be queried across flows.
- **Resubmitting to the topic without `resubmitTo`:** simpler, but it would run every subscriber again, including the Billing Logic App.

## Consequences

- Search and trails lag by Application Insights' ingestion delay, usually a few minutes. The failed runs and dead-letter queue panels read live, so a failure shows up within seconds.
- The console only works as well as the tracking contract is followed, so the contract applies to every flow from P1 on, before the console exists.
- Every Service Bus subscription needs the `resubmitTo` clause in its filter, which rules out plain correlation filters.
- Retention is set once, by `integrationRetentionDays` in the estate Bicep, for Application Insights, run history and the transit containers.
- The console has no Domain project or database, a stated deviation from coding guidelines §1.
