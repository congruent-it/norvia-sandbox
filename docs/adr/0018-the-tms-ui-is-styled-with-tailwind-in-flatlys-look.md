# The TMS UI is styled with Tailwind, in Flatly's look

The TMS UI keeps Fluent UI for its components (data grids, message bars, progress rings) but is styled with Tailwind CSS, to look like Bootswatch's Flatly theme: a navy navbar, teal links, Lato, flat cards and tables. Fluent UI's own look is Microsoft's, and demo audiences should see a freight company's tool, not an admin portal. Flatly's palette is carried over by hand as Tailwind theme colours. Bootstrap itself is not used.

Tailwind replaces Fluent UI's `reboot.css` with its own preflight, because the reboot sits outside any cascade layer and would win over every Tailwind utility. Fluent's scoped component styles are also unlayered, so the few overrides of them (tables, message bars) are unlayered too, with just enough specificity to outrank Fluent's. Fluent's accent colour is set to Flatly's navy through `FluentDesignTheme`, so Fluent's own components match.

## Considered Options

- **Fluent UI design tokens only** (coding guidelines §9 as written): no new tooling, but an accent colour and a font are all it changes, which is not enough for Flatly's look.
- **Bootswatch's Flatly stylesheet with Bootstrap:** the exact theme, but Bootstrap's reset and component classes clash with Fluent UI, and it's a second component library.
- **Hand-written CSS:** no build step, but every spacing, colour and breakpoint is a one-off.

## Consequences

- Building the TMS UI needs Node.js: the build runs `npm ci` and the Tailwind CLI (see `Norvia.Tms.Web.csproj`). `wwwroot/app.css` and the Lato font files are build output and are not committed.
- Layout and the app shell (navbar, container, cards) are markup with Tailwind classes rather than Fluent UI's layout components. Fluent UI still provides every component with behaviour.
- A Fluent UI upgrade can change the class names or selectors the overrides rely on (`.fluent-data-grid`, `.col-title`, `.fluent-messagebar.intent-*`).
- This is a stated deviation from coding guidelines §9 for the TMS UI. Other applications that should look the same reuse this setup.
