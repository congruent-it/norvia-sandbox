# The ERP UI is styled with Tailwind, in Cerulean's look

The ERP UI keeps Fluent UI for its components (data grids, message bars) but is styled with Tailwind CSS, to look like Bootswatch's Cerulean theme: a sky-blue navbar, Source Sans, light surfaces, flat tables. It follows the TMS UI's setup ([ADR 0018](0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md)), the Partner portal's ([ADR 0022](0022-the-partner-portal-is-styled-with-tailwind-in-luxs-look.md)) and the Integration console's ([ADR 0025](0025-the-integration-console-is-styled-with-tailwind-in-darklys-look.md)): each Norvia application has a look of its own, so a demo audience sees at a glance which system it is in. Cerulean is the one of those themes that reads as a finance back office, and no other application uses it. Fluent UI's accent colour is set to Cerulean's blue through `FluentDesignTheme`.

Until now the ERP had no stylesheet of its own, so it showed the browser's defaults, and the hidden Blazor error bar (`#blazor-error-ui`) had no rule to hide it and showed permanently.

## Considered Options

- **Yeti:** a sharper look with more contrast in tables. Cerulean's calmer look was preferred.
- **Fluent UI design tokens only** (coding guidelines §9 as written): no new tooling, but an accent colour is not enough for a look.
- **Bootswatch's Cerulean stylesheet with Bootstrap:** the exact theme, but Bootstrap clashes with Fluent UI and is a second component library.

## Consequences

- Building the ERP UI needs Node.js, as the TMS UI does: the build runs `npm ci` and the Tailwind CLI (see `Norvia.Erp.Web.csproj`). `wwwroot/app.css` and the font files are build output and are not committed.
- The navigation is plain links with Tailwind classes rather than Fluent UI anchors.
- This is a stated deviation from coding guidelines §9 for the ERP UI, like the other applications'.
