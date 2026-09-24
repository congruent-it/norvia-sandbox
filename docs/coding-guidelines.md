# Coding guidelines

How code is written in the Norvia sandbox. These rules apply to every .NET application in the solution (TMS, ERP, WMS, Driver app, Partner portal, Integration console) and to the tests that cover them. The [tracking contract](#tracking-contract) also applies to the estate: API Management policies, Logic App workflows and Service Bus subscriptions. Architectural decisions live in [docs/adr/](adr/); where a rule here follows from one, it links to it.

A rule marked **must** is enforced in review or CI. A rule marked **should** is the default; deviate only with a reason stated in the PR.

## 1. Architecture: Clean Architecture

Every application **must** follow Clean Architecture, in the style of the Ardalis Clean Architecture template. The business rules sit at the centre and know nothing about databases, messaging, HTTP or UI; everything technical sits at the edge and depends inwards.

### The dependency rule

Source code dependencies point inwards only. An inner layer never references an outer one, not even through a type in a method signature.

```
  Presentation (Api, Web)  ──►  Application (use cases)  ──►  Domain
           │                              ▲                       ▲
           └──── composition root ───►  Infrastructure ───────────┘
```

| Layer | Project | Contains | May reference |
|---|---|---|---|
| Domain | `<App>.Domain` | Entities, value objects, aggregate roots, domain events, domain services, specifications | `Ardalis.SharedKernel`, `Ardalis.Specification`, nothing else in the solution |
| Application | `<App>.Application` | Use cases (one command or query plus its handler each), DTOs, validators, ports (interfaces for everything outside: clock, event publisher, outside APIs) | Domain |
| Infrastructure | `<App>.Infrastructure` | Adapters that implement the ports: EF Core `DbContext`, `RepositoryBase<T>` implementation, outbox and Service Bus publisher, typed HTTP clients | Application, Domain |
| Presentation | `<App>.Api` | Minimal API endpoints that translate HTTP to use cases and back | Application; Infrastructure only in `Program.cs` for registration |
| Presentation | `<App>.Web` | Blazor web app, if the application has a UI | Application; Infrastructure only in `Program.cs` for registration |

### Rules

- The Domain project has no dependency on EF Core, ASP.NET Core, Azure SDKs or any other framework. Persistence mapping lives in Infrastructure (§5).
- Ports are defined by the layer that needs them (Application or Domain) and implemented in Infrastructure. Handlers depend on the interface, never on the adapter.
- Every use case is its own command or query record with a single handler, dispatched through a mediator (the `Mediator` source-generated library). Commands change state and return at most an ID or a result; queries return DTOs and never change state.
- Handlers orchestrate: load aggregates through a repository, call domain methods, save, and return. Business decisions belong in the domain, not in the handler.
- Presentation layers only translate: bind and validate input, send a command or query, map the result to a response or a view. No business logic and no data access.
- Domain entities never cross the Application boundary outwards. Endpoints and components receive DTOs.
- Cross-cutting behaviour (validation, logging, transactions) is implemented once as mediator pipeline behaviours, not repeated in each handler.
- Organise code inside each project by feature (`TransportOrders/Create/`, `Trips/Dispatch/`), not by technical type (`Commands/`, `Handlers/`).
- The dependency rule **must** be enforced by architecture tests (NetArchTest) in `<App>.ArchitectureTests`, which run with the unit tests.

## 2. Solution structure

- Test projects mirror the project they test: `<App>.Domain.UnitTests`, `<App>.Application.UnitTests`, `<App>.ArchitectureTests`, `<App>.Api.IntegrationTests`, `<App>.Web.UiTests`.
- Applications **must not** reference each other's projects. They meet only through Azure Integration Services and the Canonical Model ([ADR 0001](adr/0001-realistic-applications-integrated-only-through-ais.md)).
- Local orchestration goes through the Aspire AppHost; every service references the shared `ServiceDefaults` project ([ADR 0004](adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)).
- Package versions are managed centrally in `Directory.Packages.props`. Project files **must not** carry version numbers.
- Shared build settings live in `Directory.Build.props`:

  ```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  ```

## 3. C# style

Formatting and naming are enforced by the root `.editorconfig` and `dotnet format`. Beyond that:

- Use file-scoped namespaces, one public type per file, file named after the type.
- Classes are `sealed` unless they are designed for inheritance.
- Use `record` types for DTOs, commands, queries and events. Use `init` or constructor parameters; public setters on domain types are not allowed.
- Nullable reference types are on everywhere. Do not suppress warnings with `!` unless the reason is obvious from the line itself.
- Async all the way: every I/O method is `async`, ends in `Async` and accepts a `CancellationToken` as its last parameter, which it passes on. Never call `.Result`, `.Wait()` or `GetAwaiter().GetResult()`.
- Do not use exceptions for expected outcomes (not found, validation failed, invalid state transition). Return a result type and let the endpoint map it to a status code. Exceptions are for bugs and infrastructure failures.
- Prefer small, intention-revealing methods over comments. Comment *why*, not *what*.
- Use the domain terms from [CONTEXT.md](../CONTEXT.md) in type, method and variable names. If a term is missing, add it there before coding it.

## 4. Domain model

- Business rules live in the domain entities and value objects, not in handlers, endpoints or components.
- An aggregate root protects its own invariants. State changes go through methods that describe the business action (`shipment.MarkDelivered(proof)`), not through property setters.
- Aggregate roots implement `Ardalis.SharedKernel`'s `IAggregateRoot` (or the solution's equivalent marker interface). Only aggregate roots get a repository.
- Aggregates raise domain events for things other parts of the application must react to. Integration events for other applications are derived from these (see §6).
- Use strongly typed IDs (`TransportOrderId`) instead of raw `Guid` or `int` for aggregate identities.

## 5. Persistence: EF Core with Ardalis repositories and specifications

- Data access **must** go through `Ardalis.Specification` and `Ardalis.Specification.EntityFrameworkCore`.
  - Define `IRepository<T>` and `IReadRepository<T>` constrained to `IAggregateRoot`, implemented once in Infrastructure by deriving from `RepositoryBase<T>`.
  - Command handlers use `IRepository<T>`. Query handlers use `IReadRepository<T>`.
- Every non-trivial query is a named specification in the Domain project (`OpenShipmentsForCarrierSpec`, `TripWithStopsByIdSpec`). Specifications own filtering, includes, ordering and paging.
- Use `Specification<T, TResult>` with `Select` for read models, so queries fetch only the columns they need.
- Read-only queries set `AsNoTracking()` in the specification.
- `DbContext` and `IQueryable<T>` **must not** leak out of the Infrastructure project. No LINQ against the `DbContext` in handlers or endpoints.
- Do not create generic "get everything" repository methods or pass raw expressions into repositories; add a specification instead.
- One `DbContext` per application, owning that application's database only. An application never reads another application's database ([ADR 0002](adr/0002-one-system-of-record-per-concept.md)).
- Entity configuration lives in `IEntityTypeConfiguration<T>` classes, not in attributes on domain types.
- Schema changes are EF Core migrations, committed with the change that needs them. Migrations are applied by a deployment step, not at application startup in production.
- Use optimistic concurrency (a row version) on aggregates that can be changed by more than one caller.

## 6. APIs and integration

The rule of the estate is **HTTP in, events out** ([ADR 0003](adr/0003-http-in-events-out.md)): applications receive commands as HTTP calls to their own API and publish what happened as events to Service Bus. Applications do not consume from Service Bus.

- Logic Apps **must not** be used for read actions:
  - A query is routed by API Management straight to the application's API. The APIM policy may change the format, but no Logic App sits on the path.
  - A Logic App never calls an application's API to look data up. An event carries everything its consumers need; if a consumer needs more, add it to the event.
  - Fetching a claim-check blob from a transit container is part of delivering a command, not a read action.
- Apart from the application's own UI, its API therefore has two kinds of callers: Logic Apps for commands, and API Management for queries. Authorize each operation for the caller it is designed for.

### HTTP APIs

- Use Minimal APIs, grouped per resource with `MapGroup`, and keep endpoints thin: bind, validate, call a handler, map the result.
- Version every API from the start (`/v1/...`) with `Asp.Versioning`.
- Return errors as RFC 9457 `ProblemDetails`. Never return stack traces or exception messages to callers.
- Validate input at the boundary (FluentValidation or endpoint filters). Invalid requests get a `400` with the validation errors.
- Commands **must** be idempotent. Logic Apps and APIM retry, so the same command can arrive more than once; accept an idempotency key or make the operation naturally idempotent.
- Long-running or asynchronous work returns `202 Accepted` with a way to find the outcome ([ADR 0008](adr/0008-carrier-tracking-events-are-accepted-asynchronously.md)).
- Publish an OpenAPI document for every API; APIM imports it.
- Never trust caller identity from a payload. Identity comes from the validated token ([ADR 0005](adr/0005-partner-identity-comes-from-the-token.md)).

### Events

- Events are published only after the change that caused them is committed. Use a transactional outbox so a rolled-back change never produces an event and a committed change never loses one.
- Event contracts follow the Canonical Model, are named in the past tense (`ShipmentDelivered`) and are versioned. Changes to a published contract are additive; a breaking change is a new version.
- Every event carries an event ID, a timestamp, the aggregate ID and a correlation ID.
- Large payloads such as documents use a claim check, not the message body ([ADR 0009](adr/0009-carrier-proofs-of-delivery-use-a-claim-check.md)).

### Outbound HTTP

- Use typed `HttpClient`s registered through `IHttpClientFactory`, with the standard resilience handler from `Microsoft.Extensions.Http.Resilience` (timeouts, retries with jitter, circuit breaker). `ServiceDefaults` wires this up.
- Never `new HttpClient()` in application code.

## 7. Observability

- Logging, tracing and metrics go through OpenTelemetry as configured in `ServiceDefaults`. Do not add another logging framework.
- Use structured logging with message templates (`logger.LogInformation("Shipment {ShipmentId} delivered", id)`), never string interpolation. Use `LoggerMessage` source generators on hot paths.
- Propagate the correlation ID across HTTP calls and into published events so one message can be followed end to end through the Logic App run history.
- Do not log secrets, tokens or personal data (Consignee names, addresses, phone numbers).
- Every service exposes health checks (`/health` and `/alive` via `ServiceDefaults`) that cover its database and outbound dependencies.

### Tracking contract

Every flow records the same keys under the same names, so the [Integration console](operations/integration-console.md) can find any Message trail by correlation ID or business key ([ADR 0017](adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). This applies from the first flow, whether or not the console exists yet.

| Key | Holds |
|---|---|
| `correlationId` | The Message trail's correlation ID |
| `flow` | A stable flow name such as `transport-order-intake`, `carrier-tracking` or `billing`, not the workflow's resource name |
| `partyId` | The Shipper or Carrier ID from the token (ADR 0005) |
| `transportOrderNumber`, `shipmentNumber`, `carrierBookingNumber`, `tripNumber` | The business keys the message carries |
| `sourceMessageId` | The Carrier's event ID or document ID, or the Driver app event ID |
| `outcome` | `Succeeded`, `Refused`, `Retrying`, `DeadLettered` or `Failed` |
| `failureCode` | The stable failure code, when there is one |

A key that doesn't apply is left out, never sent empty.

- **Logic Apps** **must** set these as tracked properties on the trigger and on the step that calls the application, set the run's client tracking ID to the correlation ID, and send their telemetry to the estate's Application Insights.
- **Every message put on Service Bus** (by an API Management policy, a Logic App or an application's outbox) **must** carry the keys it knows as application properties, so a message dead-lettered by Service Bus itself, for example after its maximum delivery count, can still be found.
- **A Logic App that dead-letters a message** sets `DeadLetterReason` to the failure code and `DeadLetterErrorDescription` to the readable message.
- **Applications** add the keys they know to their logging scope, so their traces join the trail.
- **Secure inputs and outputs** are used only on steps that handle secrets. Anywhere else they hide the body the Integration operator needs.
- **Every Service Bus subscription's filter** is a SQL filter that also handles resubmits from the console: `(<the subscription's own condition> AND resubmitTo IS NULL) OR resubmitTo = '<subscription name>'`. A resubmitted message then reaches only the subscription it was dead-lettered from.
- **Retention** of Application Insights, Logic Apps run history and the integration layer's transit containers is set by the one estate Bicep parameter `integrationRetentionDays` (90 by default), never per resource.

## 8. Configuration, secrets and security

- Bind configuration to options classes with `ValidateDataAnnotations()` and `ValidateOnStart()`, so a misconfigured service fails at startup, not on the first request.
- No secrets in source, `appsettings*.json` or environment files. In Azure, services authenticate with managed identity and read remaining secrets from Key Vault; locally, use user secrets or Aspire parameters.
- Every API endpoint requires authorization unless explicitly marked otherwise with a reason. Authorize on app roles or scopes, not on the mere presence of a token.
- Keep dependencies current; CI fails on known vulnerable packages (`dotnet list package --vulnerable`).

## 9. Web applications: Blazor

- Web UIs **must** be built with Blazor (Blazor Web App template). Choose the render mode per page or component: static SSR by default, Interactive Server where interactivity is needed, and WebAssembly only with a stated reason.
- UI components **must** come from the Fluent UI Blazor library (`Microsoft.FluentUI.AspNetCore.Components`): layout, navigation, data grids, forms, dialogs and messages. Write a component of your own only when Fluent UI has nothing that fits, and build it from Fluent UI components and design tokens so every application looks the same. Don't add another component library or CSS framework.
- Components render and handle UI events. Business rules stay in the domain; data access goes through application services or typed API clients, never a `DbContext` in a component.
- Keep components small. Split a component when it has more than one reason to change; move logic that is not about rendering into a plain C# class that can be unit tested.
- Use `[EditorRequired]` on parameters a component cannot work without.
- Use `EditForm` with a validator and Fluent UI input components for forms; show validation messages next to the field.
- Build for accessibility: semantic HTML, labels on every input, keyboard-operable controls. Playwright tests locate elements by role and label, so accessible markup is also testable markup.
- Add `data-testid` attributes only where no role or label identifies an element uniquely.
- Handle loading, empty and error states explicitly in every component that fetches data.

## 10. Testing

### Unit tests

- Use xUnit for all test projects.
- Use **NSubstitute** for test doubles. Do not add another mocking library.
  - Substitute interfaces, not concrete classes.
  - Substitute only what crosses a boundary of the unit under test (repositories, publishers, clocks, HTTP clients). Do not substitute domain entities or value objects; construct real ones.
  - Prefer asserting on outcomes over `Received()` checks. Use `Received()` when the call itself is the behaviour being tested, such as "publishes `ShipmentDelivered`".
- Handlers are tested with `IRepository<T>` / `IReadRepository<T>` substituted by NSubstitute; configure them with `Arg.Any<ISpecification<T>>()` or `Arg.Is<SomeSpec>(...)`.
- Specifications are tested without a database by evaluating them in memory against a list of entities (`spec.Evaluate(entities)`).
- Blazor components are unit tested with bUnit, with services they depend on substituted by NSubstitute.
- Inject time (`TimeProvider`) and randomness instead of calling `DateTime.UtcNow` or `Guid.NewGuid()` directly, so tests are deterministic.
- Name tests `Method_Scenario_ExpectedOutcome` and structure them Arrange / Act / Assert. One behaviour per test.
- Unit tests do not touch the network, file system or a database, and run in milliseconds.

### Code coverage

- Unit tests **must** reach at least **80% line coverage** per production project. CI fails the build below that threshold.
- Coverage is measured with coverlet over unit test projects only; integration and UI tests do not count towards the threshold. Configure it once in `Directory.Build.props` for test projects:

  ```xml
  <PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.UnitTests'))">
    <CollectCoverage>true</CollectCoverage>
    <CoverletOutputFormat>cobertura</CoverletOutputFormat>
    <Threshold>80</Threshold>
    <ThresholdType>line</ThresholdType>
    <ThresholdStat>minimum</ThresholdStat>
  </PropertyGroup>
  ```

- Branch coverage is reported in the PR but not gated.
- Exclusions are limited to code with no behaviour to test: EF migrations, generated code, `Program.cs` composition roots and the Aspire AppHost. Mark them with `[ExcludeFromCodeCoverage(Justification = "...")]` or exclude them in the coverlet settings; never exclude hand-written logic to reach the threshold.
- Coverage is a floor, not a goal. A test that executes code without asserting its behaviour does not count in review.

### Integration tests

- API integration tests run the real application with `WebApplicationFactory` or `Aspire.Hosting.Testing`, against a real database in a container (Testcontainers), not the EF in-memory provider.
- Outbound dependencies outside the application (Service Bus, other APIs) are replaced at the edge, and tests assert on the events the application would publish.

### UI tests

- End-to-end UI tests **must** use **Playwright** for .NET (`Microsoft.Playwright`).
- Locate elements with `GetByRole`, `GetByLabel` and `GetByText`; fall back to `GetByTestId`. Never use CSS or XPath selectors tied to layout.
- Rely on Playwright's auto-waiting and web-first assertions (`Expect(locator).ToBeVisibleAsync()`). Never use `Thread.Sleep`, `Task.Delay` or fixed timeouts.
- Each test sets up its own data and does not depend on the order of other tests.
- Record traces, screenshots and video on failure and publish them as CI artifacts.
- Keep UI tests to the critical user journeys; cover edge cases lower in the pyramid.

## 11. Workflow

- Every change goes through a pull request with at least one review.
- A PR is mergeable only when the build has no warnings, `dotnet format --verify-no-changes` passes, all tests pass and the coverage gate is met.
- Tests are part of the change, not a follow-up. Bug fixes start with a failing test that reproduces the bug.
- A decision that is hard to reverse, surprising, or the result of a real trade-off gets an ADR in [docs/adr/](adr/).
- Keep PRs small and focused on one issue.
