# Norvia icon

Every Norvia web UI (TMS, ERP, Partner portal, Integration console, Simulator, Driver app) shows the same tab icon: an N drawn as a route, with the destination in orange. It has a transparent background and follows the browser's light or dark scheme, so it reads on any tab and beside any UI's look.

## Where it lives

The shared library [src/Norvia.Branding](../src/Norvia.Branding) holds it:

| File | Use |
|---|---|
| `wwwroot/favicon.svg` | **The source.** Edit this one. It carries its own light and dark colours (`prefers-color-scheme`) |
| `wwwroot/favicon.ico` | Fallback for browsers that don't take an SVG: 16, 32 and 48 px on a white tile |
| `wwwroot/apple-touch-icon.png` | 180×180 px, full-bleed white, for iOS home screens |
| `NorviaFavicon.razor` | The three `<link>` tags. Each UI's `Components/App.razor` renders it in its `<head>` |

The files are static web assets, so every UI serves them from `/_content/Norvia.Branding/`. There is one copy. A UI that wants the icon references the project and renders `<Norvia.Branding.NorviaFavicon />`; it does not copy files.

## Changing the icon

1. Edit `src/Norvia.Branding/wwwroot/favicon.svg`. Check it at 16×16 px on a light and a dark tab.
2. Run `pwsh src/Norvia.Branding/export-icons.ps1` to write the ICO and the Apple touch icon from it. It needs PowerShell 7, `rsvg-convert` (librsvg) and ImageMagick (`magick`) on the PATH, on Windows too.
3. Commit the SVG and the exported files together. The unit tests check their formats and sizes.

## Without sign-in

In Azure the demo UIs sit behind Easy Auth, which sends a browser without a session to Entra. `EntraSignIn` in the AppHost leaves `/_content/Norvia.Branding/*` outside the gate for every UI, so the tab shows the icon on the sign-in redirect as well. The icons are no secret. Locally there is no gate.
