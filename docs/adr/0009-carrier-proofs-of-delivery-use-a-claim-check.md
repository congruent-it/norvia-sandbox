# Carrier Proofs of delivery use a claim check

A Carrier's Proof of delivery carries files (a signed CMR, photos) that are too large to travel on Service Bus. API Management stores the incoming Proof of delivery in an inbound blob container, puts a small message with the blob reference, the Carrier ID and a correlation ID on a Service Bus queue for inbound Proofs of delivery, and returns `202 Accepted`. A Logic App reads the queue, fetches the blob, validates and maps it, and hands it to the TMS. The TMS keeps the Proof of delivery and its files in its own storage (ADR 0002). The inbound container is only transit. This keeps a Carrier's inbound traffic asynchronous, as for Tracking events (ADR 0008), and shows the standard pattern for moving files through a message-based estate.

## Considered Options

- **Synchronous upload through a Logic App to the TMS:** the Carrier would see every failure, but each upload would depend on the Logic App and the TMS being available, and large bodies would pass through every hop.
- **Valet key (SAS upload URL, then metadata):** the file skips API Management, but the Carrier needs two calls, and a file without metadata (or metadata without a file) becomes a new failure to detect.

## Consequences

As with Tracking events, the Carrier never learns whether its Proof of delivery arrived, so noticing a failure is Norvia's job. That takes alerts on the Logic App and the dead-letter queue, backed by a missing Proof of delivery flag on the business side. The inbound container needs a retention policy, and a blob whose message failed stays behind for investigation.
