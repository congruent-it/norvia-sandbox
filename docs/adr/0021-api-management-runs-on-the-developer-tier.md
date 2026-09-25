# API Management runs on the Developer tier

The intake path is synchronous: a Shipper's call waits in API Management while the intake Logic App retries the TMS, and gets "not received" only once the retries are spent. The Consumption tier ends a request after 30 seconds, so it would answer a TMS outage with a gateway timeout instead of the partner-format answer. We run the Developer tier, which has no such limit, at about €41 a month and a first deployment of about 45 minutes. It has no SLA, which a sandbox doesn't need.

## Considered Options

- **Consumption:** about nothing at sandbox volumes, deployed in minutes, but the 30-second limit. Shorter TMS retries, or API Management answering "not received" itself on a timeout, would work around it.
- **Basic v2:** no time limit and deployed in minutes, but about €129 a month.

## Consequences

- API Management costs money whenever it exists, used or not: it can't be stopped, only deleted and deployed again, which takes 45 minutes and a purge of the soft-deleted instance.
- Switching to Consumption later changes only the SKU in `infra/estate/partner-api.bicep`, plus one of the workarounds above.
