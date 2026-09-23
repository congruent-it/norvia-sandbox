# HTTP in, events out

Applications receive commands only as HTTP calls to their own API, made by a Logic App (or by APIM for synchronous queries). They publish what happened as events to Service Bus. No application consumes from Service Bus directly. This puts a Logic App run history on every inbound hop, so a demo can show the full path of a message and the exact place where it failed. It also keeps each application free of messaging concerns (retries, dead-lettering, sessions); those belong to the integration layer.

## Considered Options

- **Applications consume Service Bus directly:** fewer moving parts, but the inbound path is invisible in the portal, and each application reimplements dead-letter handling.
- **HTTP only, both directions:** applications would call Logic Apps to announce events, so the source application has to know who cares about them.
