# The Partner portal is styled with Tailwind, in Lux's look

The Partner portal is styled the way the TMS UI is ([ADR 0018](0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md)): Fluent UI for its components, Tailwind CSS for styling and layout. It takes the look of a different Bootswatch theme, Lux, instead of Flatly. Lux is black and white with a near-black accent, uppercase navigation and headings, square corners and Nunito Sans. In the demo the Partner portal and the TMS UI sit side by side, and the audience must see at a glance which is Norvia's tool and which is the outside party's. The Partner portal plays every Shipper and Carrier ("nothing at Norvia"), so a neutral look suits it, and it takes on no Persona's brand. Lux's palette is carried over by hand as Tailwind theme colours, and Fluent's accent colour is set to Lux's near-black through `FluentDesignTheme`. Bootstrap itself is not used.

## Considered Options

- **Flatly, like the TMS UI:** no second theme to maintain, but side by side the two applications look like one product, which is the opposite of what the demo shows.
- **A blue theme (Cosmo, Zephyr, Yeti, Cerulean):** distinct enough on their own, but close to Flatly's navy, so they read as another Norvia application.
- **Pulse (purple):** just as distinct, but louder than a neutral outside party needs.

## Consequences

- The Partner portal's build needs Node.js too: it runs `npm ci` and the Tailwind CLI (see `Norvia.PartnerPortal.Web.csproj`). `wwwroot/app.css` and the Nunito Sans font files are build output and are not committed.
- The Fluent UI overrides are written again for Lux rather than shared with the TMS UI: the two apps must not share projects (coding guidelines §2), and the looks differ anyway.
- This extends the exception to coding guidelines §9 from the TMS UI to the Partner portal.
