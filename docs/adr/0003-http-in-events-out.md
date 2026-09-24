# HTTP in, events out

Applications receive commands only as HTTP calls to their own API, made by a Logic App. They publish what happened as events to Service Bus. No application consumes from Service Bus directly. This puts a Logic App run history on every inbound hop, so a demo can show the full path of a message and the exact place where it failed. It also keeps each application free of messaging concerns (retries, dead-lettering, sessions); those belong to the integration layer.

Logic Apps are never used for read actions. A query goes from API Management straight to the application's API, and API Management's policy only changes the format. A Logic App never calls an application to look data up either: an event carries everything its consumers need, so a flow never has to ask the source application for more (ADR 0012, ADR 0015). A Logic App run then always means something was changed, and a query doesn't have to depend on a Logic App being available or pay for a run it doesn't need. Fetching a claim-check blob from a transit container (ADR 0009, ADR 0016) is part of delivering a command, not a read action.

## Considered Options

- **Applications consume Service Bus directly:** fewer moving parts, but the inbound path is invisible in the portal, and each application reimplements dead-letter handling.
- **HTTP only, both directions:** applications would call Logic Apps to announce events, so the source application has to know who cares about them.
- **Queries through a Logic App as well:** every inbound hop would have a run history, but a read would pay for a run and depend on the Logic App being up, and run histories would fill with reads that change nothing.
