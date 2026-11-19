# Diagram styles: Excalidraw

Norvia's diagrams are Excalidraw files in one look: the same palette, type and shapes. Open a `.excalidraw` file at [excalidraw.com](https://excalidraw.com) or with the VS Code Excalidraw extension. There are four layouts:

| Style | For | Example | Library |
|---|---|---|---|
| **Matrix** | Process steps against applications | [business-automation-model.excalidraw](business-automation-model.excalidraw) | [MatrixDiagram.cs](../../../src/Docs/Norvia.Docs.Diagrams/MatrixDiagram.cs) |
| **Grid** | Who has what: rows of identities against columns of resources | [identity-and-access.md](../identity-and-access.md) | [GridDiagram.cs](../../../src/Docs/Norvia.Docs.Diagrams/GridDiagram.cs) |
| **Sequence** | One flow across several applications, in time order | [order-lifecycle.md](../order-lifecycle.md) | [SequenceDiagram.cs](../../../src/Docs/Norvia.Docs.Diagrams/SequenceDiagram.cs) |
| **Graph** | System context, containers and components (the C4 levels), context maps, hexagons and state diagrams | [c4.md](../c4.md), [context-map.md](../context-map.md), [tms-architecture.md](../tms-architecture.md), [tms-lifecycles.md](../tms-lifecycles.md) | [GraphDiagram.cs](../../../src/Docs/Norvia.Docs.Diagrams/GraphDiagram.cs) |

All four draw through [Canvas.cs](../../../src/Docs/Norvia.Docs.Diagrams/Canvas.cs), which holds the drawing primitives, and take their colours from `Palette` ([Palette.cs](../../../src/Docs/Norvia.Docs.Diagrams/Palette.cs)). The libraries are one class library, `Norvia.Docs.Diagrams`; each diagram is a file-based .NET app that references it. The matrix style is described first, the graph style [below](#graph-style).

Diagrams are generated from a spec, not drawn by hand, so a process change is an edit to a list and a re-run. Hand edits to the `.excalidraw` file are lost on the next run.

A spec is a file-based app (`dotnet run <file>.cs`, .NET 10): its first line, `#:project`, points at the class library, and the rest builds a spec object and calls a style. The `.excalidraw` output is deterministic: the seeds are drawn from a fixed generator and the numbers print the way they always have, so a re-run changes a file only when its spec changed. `git diff` after a run shows exactly what moved.

| File | Is |
|---|---|
| [MatrixDiagram.cs](../../../src/Docs/Norvia.Docs.Diagrams/MatrixDiagram.cs) | The matrix style: geometry and routing. `MatrixDiagram.Render(spec, "out.excalidraw", "preview.svg")` |
| [business-automation-model.cs](business-automation-model.cs) | The reference spec. Copy it for a new diagram |

```
dotnet run docs/architecture/diagrams/business-automation-model.cs -- preview.svg
rsvg-convert -w 2600 preview.svg -o preview.png     # check the layout; the SVG is a preview, not the deliverable
```

## Layout

- **Rows are applications** (lanes), in this order: outside parties on top, the integration layer next, then Norvia's systems, planned ones last (dashed). Applications never call each other, so every hand-off crosses the integration layer row.
- **Columns are process steps**, grouped by process (one column header each, with its process IDs) and by value stream (the coloured band above). A step sits in the lane of the application that performs it.
- **Cross-cutting actors** (Integration console, Simulator) are full-width bands under the lanes, not boxes in a column.
- **Title and legend** at the top, a footnote with the sources at the bottom.
- A box holds a **title** (an action, in the glossary's terms, at most two lines) and a **subtitle** (process ID, then the component: `P4 · two Logic Apps`).
- Keep one flow chain per column. When two chains would share a column, give the second its own.

## Colour

| Meaning | Look |
|---|---|
| A person acts | Yellow `#fff3bf`, amber outline. The same in every lane, so the manual touchpoints stand out |
| The application or an integration flow does it | The lane's own tint and outline |
| Planned, or reference data | Grey, dashed |

Each application keeps one colour in every diagram: Partner portal grey, Integration layer violet, TMS blue, ERP green, Driver app teal, Integration console pink, Simulator orange. Value streams use blue, green and orange. The tokens are `Palette.Tones` in the library; add a colour there, never inline.

## Arrows

- Solid dark grey for a built flow, dashed light grey for a planned flow or reference data. Arrowheads on both ends for a request and its answer.
- Four routes, chosen per edge: `v` straight up or down inside a column, `h` right to the next column with a jog, `gr` and `gl` leave and enter on the right or left side and run in the gutter between columns, for edges that would otherwise cross a box. Several edges in one gutter take different `off` offsets, and `sdy` and `ddy` shift where they meet a box.
- A branch (one step leading to two) shares the first segment and carries a small label (`EdgeLabels`).

## Type and shapes

Nunito (Excalidraw font 6), roughness 0, rounded rectangles, 2 px outlines, no shadows. Titles bold 15, subtitles 12, headers 18. Text is wrapped by the generator at a conservative width, so titles stay within two lines: shorten the text before widening a box.

## Spec

A `MatrixSpec` (see the reference spec for a full example):

| Property | Holds |
|---|---|
| `Title`, `Subtitle`, `Footnote` | Header and sources line |
| `Lanes` | `MatrixLane(key, palette, name, sub, Dashed: true)` |
| `Bands` | `MatrixBand(name, palette, [new(column name, process IDs), ...])`, one per value stream. Columns number from 0 across the bands in order |
| `Cross` | Full-width bands: `MatrixCross(name, sub, palette, headline, detail)` |
| `Steps` | `MatrixStep(id, column, lane key, slot, kind, title, subtitle)`. `slot` stacks boxes in a lane, from 0. `kind` is `person`, `auto` or `planned` |
| `Edges` | `MatrixEdge(from id, to id, route, Dashed:, Both:, Off:, Sdy:, Ddy:)` |
| `EdgeLabels` | `MatrixLabel(text, step id, dx, dy)` |
| `Legend` | Optional; defaults to the four entries above |
| `ColumnWidth`, `BoxHeight` | Optional; bigger columns and boxes for a spec with more to say per box |

## Sequence style

For one flow across several applications, from [order-lifecycle.cs](order-lifecycle.cs). Participants run across the top, time runs down, and each message is an arrow with its label on it. It uses the same colours, so a participant is coloured by its application.

| Key | Holds |
|---|---|
| `Participants` | `SequenceParticipant(id, title, palette, sub, kind)`; `kind: "person"` for a person |
| `Groups` | Optional: `SequenceGroup(label, [ids])`, a band over consecutive participants |
| `Steps` | `new Phase(title)` for a band, `new Message(from, to, label, Dashed:, Both:)` for an arrow, `new SelfStep(id, label)` for what a participant does on its own, `new Note([ids], text)` for a note across participants |

- A solid arrow is a call, a dashed one is a reply or an event. Pass `Dashed: true`.
- Every arrow between two applications goes through the integration layer, so show it: a Logic App or API Management is a participant between them, never skipped.
- The sign-in flows of [identity-and-access.md](../identity-and-access.md#how-people-sign-in) are sequence diagrams too, in [sign-in.cs](sign-in.cs). A person or a Persona is a participant, and the last phase of each is **Locally**.
- The Message trails of the process docs are sequence diagrams too, in [message-trails.cs](message-trails.cs): one spec per trail, named after its doc (`p9-3` is the third trail in P9). A participant is a Hop, named `application: operation`, and a Flow draws as a band over its Hops. Edit the spec and run the script, as for the rest.
- Keep one diagram to one story and about eight participants. Split a longer flow by phase, as the order lifecycle does.

## Grid style

For who-has-what tables, from [access.cs](access.cs). Rows are the identities, grouped, and columns are the resources they may use. Each cell holds chips, one per grant, coloured by what the grant lets the row do: blue for an API role, green for reading or receiving, orange for sending or writing. A row that is nearly empty is the point: least privilege is visible.

| Key | Holds |
|---|---|
| `Columns` | `GridColumn(title, sub, palette)` |
| `Groups` | `GridGroup(title, palette, rows)`, with rows `GridRow(id, title, sub)` |
| `Cells` | `{(row id, column index): [GridChip(text, kind), ...]}` with `kind` `"role"`, `"read"` or `"write"` |

The failure patterns in [resilience.cs](resilience.cs) are a matrix, drawn with larger columns and boxes: a matrix spec may set `ColumnWidth` and `BoxHeight` and its own `Legend` when its boxes say more.

## Entity relationships

The database sections of the [application pages](../../applications/README.md) show their tables as a graph-style picture, drawn by [entities.cs](entities.cs) from `er-<slug>.json`. That JSON is not written by hand: `Norvia.Docs.Generator` writes it from the EF Core model, so `--check` fails when it is stale, and also when the `.excalidraw` file no longer draws the model's tables. After a schema change, run the generator, then `dotnet run docs/architecture/diagrams/entities.cs --` (it runs `rsvg-convert` for the PNG, as below), and commit the JSON, `.excalidraw` and `.png` together. `entities.cs` lays the tables out itself: a table sits under the table it references, and tables with no foreign keys sit in rows below. The PNG, and the columns and arrows inside the `.excalidraw`, are what `--check` cannot see go stale.

## BPMN

The process models in [docs/processes/tms](../../processes/tms/) and [docs/processes/erp](../../processes/erp/) are BPMN files, which stay the source: they open in any BPMN tool and carry their own layout. [bpmn.cs](bpmn.cs) draws each one in this style so a process page can show it: a person's task is yellow, a system's task takes its lane's colour, events are rings, gateways are diamonds, and message flows are dashed. A party outside Norvia, such as a Persona in the Partner portal, is a collapsed pool at the foot of the diagram, which the message flows run to and from. The layout in the file is kept.

```
dotnet run docs/architecture/diagrams/bpmn.cs -- [process ...]   # writes bpmn-<process>.excalidraw and .png here
```

Change a process in its `.bpmn` and run the app.

## Regenerating

```
dotnet run docs/architecture/diagrams/<spec>.cs -- [name ...]   # writes the .excalidraw and the .png next to the spec, in this folder
```

The generator writes the SVG preview to the temp folder and turns it into the PNG a Markdown page embeds with `rsvg-convert` (the dev container installs it; without it the run writes the `.excalidraw` only and says so). The PNG is rendered from that SVG, not exported from Excalidraw, so its font differs slightly from the `.excalidraw` file's. Commit both.

The `.excalidraw` files are the contract, so check a change to the drawing code by regenerating every diagram and looking at the diff: `git diff --stat -- 'docs/**/*.excalidraw'` is empty when nothing moved. (The PNGs are not part of that check: their bytes depend on the `rsvg-convert` version.)

## When you add a diagram

Terms in titles come from [GLOSSARY.md](../../../GLOSSARY.md). Copy the closest spec (a `.cs` file beside this README) and change its content, or, for a new style of picture, add the style to `Norvia.Docs.Diagrams` with unit tests. Run it, so the `.excalidraw` and the `.png` land beside it, link both from the page that explains the diagram, and add a row to [the architecture README](../README.md).

## Graph style

For the C4 diagrams in [c4.md](../c4.md), from [c4.cs](c4.cs), and for the context map, the TMS hexagon and the state diagrams, from [bounded-contexts.cs](bounded-contexts.cs), one spec per diagram. It looks like the matrix style (same colours, boxes, type, arrows, legend), but nodes sit on a grid and groups draw boundaries instead of lanes.

```
dotnet run docs/architecture/diagrams/c4.cs -- [name ...]                # writes docs/architecture/diagrams/c4-<name>.excalidraw and .png; no names draws all
dotnet run docs/architecture/diagrams/bounded-contexts.cs -- [name ...]  # writes docs/architecture/diagrams/<name>.excalidraw and .png
dotnet run docs/architecture/diagrams/order-lifecycle.cs -- [name ...]   # writes docs/architecture/diagrams/order-lifecycle-<name>.excalidraw and .png
dotnet run docs/architecture/diagrams/deployment.cs -- [name ...]        # writes the deployment view and the local-versus-Azure map
dotnet run docs/architecture/diagrams/message-trails.cs -- [name ...]    # writes the Message trails of the process docs
dotnet run docs/architecture/diagrams/access.cs --                       # writes the identity and access grid
dotnet run docs/architecture/diagrams/resilience.cs --                   # writes the failure patterns
dotnet run docs/architecture/diagrams/sign-in.cs -- [name ...]           # writes the sign-in sequence diagrams
dotnet run docs/architecture/diagrams/entities.cs -- [slug ...]          # writes the entity relationship diagrams of the application pages
dotnet run docs/architecture/diagrams/bpmn.cs -- [process ...]           # writes the BPMN pictures
```

| Key | Holds |
|---|---|
| `Title`, `Subtitle`, `Footnote`, `Legend`, `Cell` | Header, legend entries (`Palette.Legend(label, palette)`), and the grid cell size in px (`Cell = (width, height)`) |
| `Nodes` | `GraphNode`: `Id`, `Col`, `Row` (fractions are fine), `Title`, optional `Sub` (use `\n` for lines), `Palette` (a `Palette.Tones` key), `Kind` (`person` or `planned`; none for a system), `W` |
| `Groups` | `GraphGroup`, a boundary around `Members` (nodes, or other groups): `Id`, `Title`, `Palette`, optional `Dashed` |
| `Edges` | `new GraphEdge(from, to, label) { Dashed = true, ... }` |

- **Colour is the application**, not the node's role: violet for the integration layer, blue for the TMS, and so on, as in the table above. A person is yellow, a planned system grey and dashed.
- **Titles** are the container's name, **subtitles** its technology in square brackets and then what it does.
- **The router** takes arrows orthogonally round boxes, spreads the arrows that share a side of a box, keeps parallel runs apart and puts each label where it covers no box. Leave it to it; lay the grid out so most arrows join neighbours.
- **Edge options**: `Dashed` (planned), `Plain` (no arrowhead), `Both`, `Orient` (`"h"` or `"v"`, to force the side an arrow leaves), `Sides` (`("r", "t")`, the side it leaves and enters), and `Via` (`"top"` or `"bottom"`) to run an arrow out of a box's side and round the outside of the drawing, for a return flow that would otherwise cross everything.
- Keep a group's members in neighbouring cells and leave about half a row between groups, or their boundaries touch.
- **A hexagon** draws the layers as nested groups, not a literal hexagon: driving adapters on the left, the Application (use cases and ports) with the Domain inside it, and the driven adapters on the right, with the outside world beyond them. Colour here is the **layer**, not the application, since the whole diagram is one application: orange for the driving adapters, teal for the Application, blue for the Domain, violet for Infrastructure, grey for what is outside, yellow for a person. Say so in the legend.
- **A state diagram** is one group per aggregate or dimension, with a dashed "Start" node. States are blue, an end state where the thing was carried out is green, an end state where nothing was carried out is grey, and a state that needs attention is orange. Label each arrow with the domain method that makes the move, and keep the guard in a table beside the picture, not in the label.
- Preview the SVG while you work: `c4.cs` writes it to `c4-<name>.svg` in the temp folder, and `rsvg-convert` makes the PNG.

The PNGs are rendered from the generator's SVG, not exported from Excalidraw, so their font differs slightly from the `.excalidraw` files'. Commit both.
