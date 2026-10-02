# Locally, a stand-in plays the Norvia Partner API

The Partner portal reaches Norvia only through the Norvia Partner API in API Management (ADR 0001, ADR 0005). Locally there is no API Management, no intake Logic App and no Entra, so under Aspire a small ASP.NET Core app, `Norvia.Integration.LocalPartnerApi`, plays both. It answers the same `POST /partner/v1/transport-orders` as API Management does, and checks the same things in the same order:

1. the Persona's subscription key (401 when it's missing or wrong),
2. a token for the Norvia Partner API (401),
3. the `TransportOrders.Submit` role (403),
4. a Shipper for the token's `azp` (403).

It creates the correlation ID. Then it does the intake Logic App's work with the Logic App's own code: `IntakeMap` maps the booking, the TMS is called as the intake identity, and `BookingResponses` relays the TMS's answer. The Partner portal calls it exactly as it calls API Management, so locally and in CI a booking travels Partner portal → Partner API → TMS. The side-by-side demo and the Playwright journeys run without Azure.

It plays the Carrier's side too: `POST /partner/v1/carrier-answers` answers as API Management and the Carrier-answer Logic App do (P4). It checks the same four things, with the role `CarrierBookings.Respond` and a Carrier for the token's `azp` in place of the Shipper's, creates a new correlation ID for the answer, and does the Logic App's work with the Logic App's own code: `CarrierAnswerMap` maps the answer, the TMS's answer operation is called as the identity holding `CarrierBookings.Answer`, and `CarrierAnswerResponses` relays the TMS's response, `BookingNotOpen` included.

Tokens are local too: the AppHost's `local-token-signing-key` signs them, as it does for the TMS. Locally the Partner portal issues each Persona's token itself, as Entra would, with the role `TransportOrders.Submit` for Shipper Personas and `CarrierBookings.Respond` for Carrier Personas. The AppHost reads the Personas from the seed file, gives each one a client ID and a subscription key for the run, and passes them to both apps. That is what `identities.bicep` and `partner-api.bicep` do in Azure.

## Considered Options

- **A fake Partner API client inside the portal:** less code, but nothing reaches the TMS locally, so there is no side-by-side demo and no end-to-end journey in CI.
- **Playwright only against the deployed estate:** proves the real path, but the journey then runs only after a deployment, outside CI, and a developer can't book locally.
- **The Logic Apps runtime and an API Management self-hosted gateway in containers:** closest to Azure, but both need an Azure connection or licence, and the start-up time and moving parts are out of proportion for a sandbox.

## Consequences

- The stand-in is a second implementation of the API Management policy in `infra/estate/partner-api/`. A change to the policy's checks, headers or answers must be made in both. The policies' own tests (`SubmitTransportOrderPolicyTests`, `AnswerCarrierBookingPolicyTests`) and the stand-in's tests check the same list. The Carrier-answer Logic App's steps are likewise in two places: its workflow and the stand-in's handler. `AnswerWorkflowTests` checks the workflow's definition, and the stand-in's tests check the map's and the relay's output and the correlation ID on the TMS call.
- The stand-in retries the TMS through the standard resilience handler rather than the workflow's retry policy, and it writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry, where the Integration console searches it (ADR 0027), so the stand-in logs the tracking keys the policy's traces and the workflow's tracked properties record.
- It runs only locally. `azd` never deploys it; in Azure the Partner portal is given API Management's address instead.
