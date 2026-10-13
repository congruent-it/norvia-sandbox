# The Integration console is styled with Tailwind, in Darkly's look

The Integration console is styled the way the TMS UI is ([ADR 0018](0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md)): Fluent UI for its components, Tailwind CSS for styling and layout. It takes the look of Bootswatch's Darkly theme: a charcoal page, a blue navbar, green links, Lato and flat surfaces. Darkly is Flatly's dark twin, so in a demo the audience sees at a glance that the console is another of Norvia's own tools, and the one for the people behind the scenes rather than for the Planner. The Partner portal (Lux) stays the outside party's. Darkly's palette is carried over by hand as Tailwind theme colours, and Fluent UI runs in dark mode with Darkly's blue as its accent, through `FluentDesignTheme`. Bootstrap itself is not used.

## Considered Options

- **Flatly, like the TMS UI:** no second theme to maintain, but side by side with the TMS UI the console looks like one of its pages. The demo is about following a message from one application to another.
- **Fluent UI's own dark theme:** no stylesheet build, but it looks like an Azure admin portal, which is what the console exists to replace for a demo audience.
- **Another dark Bootswatch theme (Cyborg, Slate, Superhero, Vapor):** each is dark, but none shares Flatly's type and shapes, so the console would stop reading as Norvia's.

## Consequences

- Building the console needs Node.js, as the TMS UI does: the build runs `npm ci` and the Tailwind CLI (see `Norvia.IntegrationConsole.Web.csproj`). `wwwroot/app.css` and the Lato font files are build output.
- Fluent UI's dark mode has its own scoped styles for message bars (`.fluent-messagebar.dark.intent-*`), so the console's overrides outrank those too.
- This is a stated deviation from coding guidelines §9 for the Integration console, as for the TMS UI and the Partner portal.

## Amendment: the Simulator

The Simulator ([ADR 0039](0039-the-simulator-books-only-through-the-estates-real-doors.md)) is styled the same way, in Darkly's look, with the same Tailwind build and Fluent UI in dark mode: it is another tool for the people behind the scenes, not a party's or an employee's application. This is the same stated deviation from coding guidelines §9.
