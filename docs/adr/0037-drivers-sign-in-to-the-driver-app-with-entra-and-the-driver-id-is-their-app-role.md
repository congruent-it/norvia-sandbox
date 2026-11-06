# Drivers sign in to the Driver app with Entra, and the Driver ID is their app role

A Driver signs in to the Driver app with their Norvia Entra account and sees only their own Trips; the Driver ID comes from the token, never from the request ([ADR 0005](0005-partner-identity-comes-from-the-token.md)). This replaces P11's picker, where anyone chose any seeded Driver.

In Azure the Driver app's screens sit behind Container Apps' built-in authentication (Easy Auth), like the Integration console ([ADR 0026](0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)), signing in to an app registration of their own, *Norvia Driver app*. Easy Auth holds no client secret, so it gets an ID token only, and that carries the user's app roles. The registration has one app role per active Driver of the seed file, with the **Driver ID as its value** (`DRV-0001`). The estate Bicep builds the roles from the seed file, so a new Driver in the seed is a new role. Each seeded Driver has an Entra test user with their role, which `infra/create-driver-test-users.ps1` makes and assigns, because the Bicep Microsoft Graph extension cannot create users. Entra signs in only users assigned a role (`appRoleAssignmentRequired`), so a stranger never reaches the app. Easy Auth passes the signed-in user in the `X-MS-CLIENT-PRINCIPAL` header, which Container Apps doesn't let a request from outside set. The Driver app turns the user's role into its `driverId` claim; every screen requires that claim, and a user without one sees a page that says so.

The presenter panel in the Partner portal lists the seeded Drivers' test users and the one password they share. The password is a demo credential for users that can do nothing but sign in to the Driver app.

Locally there is no Entra and no Easy Auth. The AppHost turns on a stand-in (`LocalSignIn:Enabled`): a sign-in page that lists the seeded Drivers and signs the chosen one in with a cookie holding the same `driverId` claim. The Driver app's own code is the same in both. The stand-in refuses to start outside Development, and while it is on, an Easy Auth header is not trusted, so the header can't be forged against a local run.

## Considered Options

- **A claim mapped from a directory extension attribute on the user:** a claim named `driverId` is the more natural shape, but the extension property and the claims mapping can't be declared in Bicep, so the estate would need more scripts and the claim wouldn't show in the registration.
- **The user's object ID mapped to a Driver in the Driver app:** the Driver app would hold a table that has to follow Entra, and the Driver ID wouldn't be in the token.
- **Sign-in in the app with Microsoft.Identity.Web:** as in ADR 0026, it works the same locally but is a second way of signing in beside Easy Auth, and without a secret it too gets an ID token only.

## Consequences

- The Driver app trusts the header because Easy Auth is in front of it. The AppHost always deploys it with Easy Auth.
- A Driver has exactly one role, their own. A user holding more would have more than one Driver ID, and the first would win. The test-users script assigns one.
- Entra needs the Driver app's callback, which exists only once Aspire has deployed it. The postdeploy hook ([infra/register-sign-in-callbacks.ps1](../../infra/register-sign-in-callbacks.ps1)) adds it and keeps it in the azd environment as `ESTATE_DRIVER_APP_REDIRECT_URIS`.
- Putting the Driver ID from the token on every event, and the TMS checking it against the Trip's Driver, come with the Driver app's actions.
