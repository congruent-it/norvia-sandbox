# The presenter can pick the Driver in the Driver app, behind their own sign-in

In Azure, a deployment can switch the Driver app to a Driver picker (`ESTATE_DRIVER_PICKER=true`, read by the AppHost). Easy Auth stays in front, but signs the presenter in to the demo UIs' registration ([ADR 0024](0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)), like the TMS UI, not to the Driver app's own. The presenter then picks which seeded Driver to act as. This amends [ADR 0037](0037-drivers-sign-in-to-the-driver-app-with-entra-and-the-driver-id-is-their-app-role.md), whose per-Driver Entra test users remain the default.

Drivers' test users made demos cumbersome: a tenant that requires multi-factor authentication (security defaults, or a Conditional Access policy) makes every test user register for it, and no repo change can exempt them. The presenter already signs in with their own account for the other demo UIs.

The picker is a page, `/pick-driver`, that lists the seeded Drivers. Its post puts the chosen Driver ID in an `HttpOnly` cookie. For a request Easy Auth signed in, a claims transformation turns a valid cookie into the same `driverId` claim a Driver's own token carries, plus a `driverName` claim for the greeting. Nothing downstream changes: every screen, report and event takes the Driver ID from the claim, never from the request ([ADR 0005](0005-partner-identity-comes-from-the-token.md)), and the policy still wants exactly one. In picker mode the user's app roles are never read as Driver IDs, because they are the demo UIs' roles. A signed-in user with no pick is sent to the picker, and a cookie that names no seeded Driver is ignored. The post is checked for the page's antiforgery token. The picker is not available with the local stand-in, which is its counterpart without Entra.

## Considered Options

- **Remove Easy Auth from the Driver app:** the same picker, but anyone with the URL could pick a Driver. Easy Auth in front costs nothing extra, since the presenter signs in to the other demo UIs already.
- **Keep the Driver's Entra users and exempt them from MFA:** needs Conditional Access (Entra ID P1), which the tenant may not have, and security defaults cannot exempt users.
- **A separate demo tenant without MFA:** cleanest, but it moves the whole estate.

## Consequences

- Anyone who can pass the presenter's gate can act as any Driver, the same trust as the TMS UI, where the presenter acts as the Planner.
- With the picker on, the Driver app signs in to the demo UIs' registration, so `register-sign-in-callbacks.sh` adds its callback there and skips the Driver app's registration. The Driver test users are not used, but nothing stops a deployment from making them.
- The cookie is a choice, not proof of identity: it counts only for a user Easy Auth has signed in.
