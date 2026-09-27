# The TMS UI reads in process and changes things through the TMS API

The TMS UI is the TMS's own front end, not another application, so ADRs 0001 and 0002 don't put the integration layer between it and the TMS's data. It reads through the TMS query side in process: it sends the Application's queries through its own mediator, straight to the TMS database. It changes things only through the TMS API, as its own identity holding `TransportOrders.Manage`, as any other caller does. So every change still passes the one place that checks roles, honours the Idempotency-Key, records the change and its events in the outbox and writes the [tracking contract](../coding-guidelines.md#tracking-contract), while the live lists, which poll, read without an extra hop or a second copy of the read models. The TMS UI takes in nothing and publishes nothing.

## Considered Options

- **Everything through the TMS API:** one way into the TMS database, and the TMS UI would need no database access. But each poll of a live list would go over HTTP with a token, and the TMS UI would map the API's read models back into the ones the query side already gives it. Nothing a read does needs what the TMS API adds.
- **Everything in process:** no HTTP and no identity for the TMS UI. But a re-rate would bypass the role check, the idempotency and the tracking the TMS API gives it, and the TMS UI would have to run the outbox publisher too.

## Consequences

- The TMS UI holds a reference to the TMS database and needs access to it in Azure, a second way into the TMS's data beside the TMS API.
- The architecture tests hold the split: the TMS UI uses Infrastructure only in its composition root and never depends on a command's namespace, such as `Submit` or `Rate`. A new command needs its namespace added there.
- The TMS API's read endpoints serve other callers, such as API Management's Shipper tracking. The TMS UI doesn't call them.
