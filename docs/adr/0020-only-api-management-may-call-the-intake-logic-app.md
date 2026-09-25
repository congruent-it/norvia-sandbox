# Only API Management may call the intake Logic App

The intake Logic App trusts the Shipper ID that API Management passes in `X-Shipper-ID` (ADR 0005), so a caller that reaches its trigger any other way could book for any Shipper. We require a token of API Management's managed identity through Easy Auth, and make the workflow's first step refuse every call that Easy Auth didn't authenticate as that identity. Logic Apps Standard can't turn off the trigger's SAS-signed callback URL, and Easy Auth has to let unauthenticated calls through to the runtime (`AllowAnonymous`), so without that step a SAS call would still run the workflow. App Service strips the `X-MS-CLIENT-PRINCIPAL-*` headers a caller sends, so the step can trust them.

## Considered Options

- **Easy Auth alone:** no workflow change, but anyone who can list the callback URL through the management plane can still book for any Shipper.
- **Inbound access restrictions to API Management's addresses:** also stops SAS calls, but API Management's outbound addresses aren't reserved for this estate on every tier, and a restriction by service tag lets in every API Management instance in Azure.
- **SAS only, API Management holding the callback URL as a Key Vault secret:** the pattern for Consumption logic apps, but a long-lived key where an identity does.

## Consequences

- A refused call still starts a run, which fails with `CallerNotApiManagement` and is tracked as `Refused`. Its trigger records the correlation ID and Shipper the caller sent, so a trail from a refused call names what the caller claimed, not what is true.
- Easy Auth's `unauthenticatedClientAction` must stay `AllowAnonymous`: with `Return401` the Azure portal can't show the workflow.
- Every workflow that API Management calls needs the same first step.
