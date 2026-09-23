# Realistic applications, integrated only through AIS

The sandbox's purpose is to demo Norvia's business processes visually: someone clicks through a UI in one application and watches the result appear in another. So the TMS, WMS and ERP are built as realistic applications with real domain logic and a UI, not as simulators or static stubs. The applications never call each other directly. Every interaction between them goes through Azure Integration Services (Logic Apps, Service Bus, API Management), because the integration layer is what the demos are meant to show, and failures in it have to show up in the business process.

## Considered Options

- **Simulators** (thin apps with credible APIs and no domain UI): cheaper, but a demo can only show messages, not a business process a non-technical viewer can follow.
- **Static stubs** (WireMock-style): can't hold state, so they can't show the most important failure, "the order exists, but its status never updated".

## Consequences

Each application is a real build with its own domain model, persistence and UI. That cost is accepted. A point-to-point call between applications is a defect, even when it would be simpler.
