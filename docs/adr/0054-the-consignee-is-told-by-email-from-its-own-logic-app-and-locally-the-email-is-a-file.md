# The Consignee is told by email from a Logic App of its own, and locally the email is a file

**Status: Proposed.** ADR 0052 gave the Consignee a tracking link and said the notification (#983) would send it and nothing else. This is how it is sent.

A Transport Order may carry a Consignee email (P1). When the Shipment is Collected, and when the Planner agrees a delivery window (P7), the Consignee is told where to track the goods. The email address is personal data and the tracking code is a credential, so neither may sit in an event, a log or a run's history, and a Consignee that can't be told must never hold up the Shipper's own notification.

## Decision

- **One event, naming only the Transport Order.** The TMS adds `ConsigneeNotificationRequested` to the outbox of the transaction that collected the Shipment or agreed the window, under that transaction's correlation ID. It carries the Transport Order and Shipment numbers and the reason (`Collected` or `DeliveryWindowAgreed`), and no address, no code and no times. A Transport Order with no usable email or no tracking code asks for nothing, and that is no error.
- **The Logic App asks the TMS for the address and the code.** `GET /v1/consignee-notifications/{transportOrderNumber}` returns both to the one identity holding `ConsigneeNotifications.Read`. A Transport Order with nothing to send is a `404`, which the Logic App treats as done. The step's output is a secure output, so a run's history holds neither.
- **A Logic App of its own, on its own subscription.** `consignee-notification` reads `ConsigneeNotificationRequested` from the `shipment-events` topic on its own subscription and filter. It follows the Shipper notification Logic App's shape (ADR 0033): it completes its message once it has read it, so a failed run isn't redelivered, and an unreadable message is dead-lettered as `ConsigneeNotificationRequestedUnreadable`. It doesn't report to the TMS and publishes nothing: a failed Consignee notification is a failed run, and the Shipper's path shares nothing with it.
- **The email is the link and nothing else.** Plain text, no Transport Order number, Shipper, Carrier or times: the tracking page shows the agreed window (ADR 0052).
- **In Azure the workflow posts the email to an Azure Communication Services Email resource** as its managed identity. The sandbox doesn't provision the resource: the endpoint and the verified sender are two estate parameters an operator sets, and with them empty the Logic App is deployed and its run fails at the send, which is tracked.
- **Locally a worker plays the Logic App** (`Norvia.Integration.LocalConsigneeNotificationWorker`), with the Logic App's own map, retry numbers and tracking keys, as ADR 0033 sets out. Its mail server is the machine: it writes each email as an `.eml` file to a folder (the system's temp folder under `norvia-consignee-mail`, or `ConsigneeNotification:Mail:PickupDirectory`), or sends over SMTP when `ConsigneeNotification:Mail:Host` is set. Nothing leaves the machine.

## Considered Options

- **The tracking code and the address in the event:** one hop fewer. But the code appears in no event (ADR 0052), and the address would sit in the Service Bus message, the Integration console's trail and every dead letter.
- **Sending the email from the TMS:** no Logic App. But ADR 0003 has the TMS publish events and the integration layer do the sending, and the TMS would then hold a mail server's credentials.
- **Reusing the Shipper notification Logic App:** one app less. But a stopped or failing Consignee email would then sit in the Shipper's path, which is the one thing this must not do.
- **A mail catcher container (such as Mailpit) locally:** a nicer inbox for the presenter. But it adds a container image to the AppHost and to the Kubernetes chart for something an `.eml` file shows just as well. It can be added later: the worker already sends over SMTP when a host is set.

## Consequences

- The estate has a fourteenth Logic App, its identity, its subscription and an app role on the TMS API (`ConsigneeNotifications.Read`). The role opens one Transport Order's address and code to one identity, as the report roles do for the other Logic Apps.
- The Consignee notification is not reported to the TMS. A failed run is in the Logic App's run history (Azure) or the Aspire dashboard's telemetry (locally), and an alert on failed runs and one on dead letters cover it as for the other flows. The Planner can read the tracking link in the TMS UI and pass it on (#982).
- A Transport Order's email can change between the event and the read: the Logic App sends to what the TMS says when it asks, and to nobody when the TMS says there is none.
- A Shipment that is Collected and then has a window agreed tells the Consignee twice, with the same link, as each is something the Consignee should know. A re-delivery that is Collected again does so too.
- The email's wording is English only.
