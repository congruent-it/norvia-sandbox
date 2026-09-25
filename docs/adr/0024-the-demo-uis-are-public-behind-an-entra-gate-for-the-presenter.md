# The demo UIs are public behind an Entra gate for the presenter

The TMS UI and the Partner portal have no login by design: the Role switcher and the Persona switcher change who you are without one, so a demo moves quickly. In Azure the presenter must still open them from their own browser, and opened as they are, anyone with the URL could book as Brouwer Dranken or read Consignees' names and phone numbers. We give both external endpoints and put Container Apps' built-in authentication (Easy Auth) in front of them, signing in to one Entra app registration for the demo UIs. Its service principal requires assignment and only the presenter is assigned, so Entra refuses everyone else, signed in to the tenant or not, before either app runs. The apps don't change and don't read the signed-in user: it is a gate for the demo environment, not a login in the domain's sense.

Easy Auth holds no client secret. Without one it asks Entra for an ID token only (the implicit flow, allowed on the registration for ID tokens alone), which is all a gate needs: the apps call nothing as the presenter. `/health` and `/alive` stay outside the gate for Container Apps' probes; they say only whether an app is up.

## Considered Options

- **A login in the apps:** the domain has no users to log in, and the switchers would then fight the login over who you are.
- **An IP allow-list on ingress:** no sign-in at all, but it breaks on venue Wi-Fi and mobile hotspots, whose addresses nobody knows in advance.
- **Fully public access:** anyone with the URL could book as a Shipper and read Consignees' personal data.
- **Easy Auth with a client secret or a managed identity's federated credential:** gets access tokens too, which nothing needs, for a secret to rotate or a second identity to manage.

## Consequences

- The redirect URIs name the apps' host names, which exist only once Aspire has deployed them. The postdeploy hook ([infra/register-demo-ui-callbacks.sh](../../infra/register-demo-ui-callbacks.sh)) adds them and keeps them in the azd environment as `ESTATE_DEMO_UI_HOSTS`, so deploying the estate again keeps them. Until the hook has run once, sign-in fails with a redirect URI mismatch.
- Whoever runs `azd up` is the presenter. Someone else presenting must be assigned the `DemoUis.Use` role on *Norvia demo UIs* in Entra ID.
- Locally nothing changes: there is no gate and no sign-in.
