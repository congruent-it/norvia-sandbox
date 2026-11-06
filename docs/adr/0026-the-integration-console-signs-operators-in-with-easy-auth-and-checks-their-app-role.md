# The Integration console signs operators in with Easy Auth and checks their app role

The Integration console shows message bodies, which hold Consignees' personal data, so only Integration operators may use it. In Azure it sits behind Container Apps' built-in authentication (Easy Auth), like the demo UIs ([ADR 0024](0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)). But it signs in to an app registration of its own, with the app roles `IntegrationConsole.Read` and `IntegrationConsole.Resubmit`. Easy Auth holds no client secret, so it gets an ID token only, and that carries the operator's app roles. It passes the signed-in user to the console in the `X-MS-CLIENT-PRINCIPAL` header, which Container Apps doesn't let a request from outside set ([Access user claims in application code](https://learn.microsoft.com/azure/container-apps/authentication#access-user-claims-in-application-code)). The console reads the operator and their roles from that header, and every page requires `IntegrationConsole.Read`. An operator without it sees a page that says which role they lack. The console then calls Azure as its own managed identity, never with the operator's token ([ADR 0017](0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)).

Entra signs in only users assigned one of the console's roles, so a stranger never reaches it. The presenter, who plays the Integration operator in demos, holds both roles.

## Considered Options

- **Sign-in in the app (Microsoft.Identity.Web, OpenID Connect):** the usual way for an ASP.NET Core app, and it works the same locally. But without a client secret it too gets an ID token only, and adding a secret or a federated credential means something to rotate or a second identity to manage. It would also be a second way of signing in, beside the demo UIs' Easy Auth.
- **The demo UIs' gate as it is:** nothing new in Entra, but it has one role and no way to tell reading from resubmitting (slice 2). Anyone who may present could also read message bodies.
- **Validating the ID token in the app, from Easy Auth's token store:** proves the claims came from Entra even without Easy Auth in front. But the token store needs a storage account, and Container Apps already strips the header from outside requests.

## Consequences

- The console trusts the header because Easy Auth is in front of it. Take Easy Auth away and anyone could send the header. The AppHost always deploys the console with it.
- Locally there is no Easy Auth. The AppHost signs the developer in as a local operator with both roles (`LocalOperator` settings), and the console refuses to start with those settings outside Development.
- The UI tests sign in the way Easy Auth does, by sending the header, so they exercise the console's real sign-in.
- Entra needs the console's callback, which exists only once Aspire has deployed it. The postdeploy hook ([infra/register-sign-in-callbacks.ps1](../../infra/register-sign-in-callbacks.ps1)) adds it and keeps it in the azd environment as `ESTATE_INTEGRATION_CONSOLE_REDIRECT_URIS`.
- Someone other than the presenter who operates the console needs `IntegrationConsole.Read` on *Norvia Integration console* in Entra ID.
