# Diagram styles: Excalidraw

Norvia's diagrams are Excalidraw files in one look: the same palette, type and shapes. Open a `.excalidraw` file at [excalidraw.com](https://excalidraw.com) or with the VS Code Excalidraw extension. There are four layouts:

| Style | For | Example | Library |
|---|---|---|---|
| **Matrix** | Process steps against applications | [business-automation-model.excalidraw](business-automation-model.excalidraw) | [excalidraw_matrix.py](excalidraw_matrix.py) |
| **Grid** | Who has what: rows of identities against columns of resources | [identity-and-access.md](../identity-and-access.md) | [excalidraw_grid.py](excalidraw_grid.py) |
| **Sequence** | One flow across several applications, in time order | [order-lifecycle.md](../order-lifecycle.md) | [excalidraw_sequence.py](excalidraw_sequence.py) |
| **Graph** | System context, containers and components (the C4 levels), context maps, hexagons and state diagrams | [c4.md](../c4.md), [context-map.md](../context-map.md), [tms-architecture.md](../tms-architecture.md), [tms-lifecycles.md](../tms-lifecycles.md) | [excalidraw_graph.py](excalidraw_graph.py) |

All four draw through [excalidraw_canvas.py](excalidraw_canvas.py), which holds the drawing primitives, and take their colours from `PALETTE` in the matrix library. The matrix style is described first, the graph style [below](#graph-style).

Diagrams are generated from a spec, not drawn by hand, so a process change is an edit to a list and a re-run. Hand edits to the `.excalidraw` file are lost on the next run.

| File | Is |
|---|---|
| [excalidraw_matrix.py](excalidraw_matrix.py) | The matrix style: palette, geometry, routing. `render(spec, out.excalidraw, out.svg)` |
| [business-automation-model.py](business-automation-model.py) | The reference spec. Copy it for a new diagram |

```
python3 docs/architecture/diagrams/business-automation-model.py preview.svg
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

Each application keeps one colour in every diagram: Partner portal grey, Integration layer violet, TMS blue, ERP green, Driver app teal, Integration console pink, Simulator orange. Value streams use blue, green and orange. The tokens are `PALETTE` in the library; add a colour there, never inline.

## Arrows

- Solid dark grey for a built flow, dashed light grey for a planned flow or reference data. Arrowheads on both ends for a request and its answer.
- Four routes, chosen per edge: `v` straight up or down inside a column, `h` right to the next column with a jog, `gr` and `gl` leave and enter on the right or left side and run in the gutter between columns, for edges that would otherwise cross a box. Several edges in one gutter take different `off` offsets, and `sdy` and `ddy` shift where they meet a box.
- A branch (one step leading to two) shares the first segment and carries a small label (`edge_labels`).

## Type and shapes

Nunito (Excalidraw font 6), roughness 0, rounded rectangles, 2 px outlines, no shadows. Titles bold 15, subtitles 12, headers 18. Text is wrapped by the generator at a conservative width, so titles stay within two lines: shorten the text before widening a box.

## Spec

`SPEC` is a dict (see the reference spec for a full example):

| Key | Holds |
|---|---|
| `title`, `subtitle`, `footnote` | Header and sources line |
| `lanes` | `key`, `palette`, `name`, `sub`, optional `dashed` |
| `streams` | `name`, `palette`, `cols`: a list of `(column name, process IDs)`. Columns number from 0 across the streams in order |
| `cross` | Full-width bands: `name`, `sub`, `palette`, `headline`, `detail` |
| `steps` | `(id, column, lane key, slot, kind, title, subtitle)`. `slot` stacks boxes in a lane, from 0. `kind` is `person`, `auto` or `planned` |
| `edges` | `(from id, to id, route, options)`, options `dashed`, `both`, `off`, `sdy`, `ddy` |
| `edge_labels` | `(text, step id, dx, dy)` |
| `legend` | Optional; defaults to the four entries above |

## Sequence style

For one flow across several applications, from [order_lifecycle.py](order_lifecycle.py). Participants run across the top, time runs down, and each message is an arrow with its label on it. It uses the same colours, so a participant is coloured by its application.

| Key | Holds |
|---|---|
| `participants` | `id`, `title`, `sub`, `palette`, and `kind="person"` for a person |
| `steps` | `("phase", title)` for a band, `("msg", from, to, label, options)` for an arrow, `("self", id, label)` for what a participant does on its own, `("note", [ids], text)` for a note across participants |

- A solid arrow is a call, a dashed one is a reply or an event. Pass `dashed=True`.
- Every arrow between two applications goes through the integration layer, so show it: a Logic App or API Management is a participant between them, never skipped.
- The Message trails of the process docs are sequence diagrams too, in [message_trails.py](message_trails.py): one spec per trail, named after its doc (`p9-3` is the third trail in P9). A participant is a Hop, named `application: operation`, and a Flow draws as a band over its Hops. Edit the spec and run the script, as for the rest.
- Keep one diagram to one story and about eight participants. Split a longer flow by phase, as the order lifecycle does.

## Grid style

For who-has-what tables, from [access.py](access.py). Rows are the identities, grouped, and columns are the resources they may use. Each cell holds chips, one per grant, coloured by what the grant lets the row do: blue for an API role, green for reading or receiving, orange for sending or writing. A row that is nearly empty is the point: least privilege is visible.

| Key | Holds |
|---|---|
| `columns` | `title`, `sub`, `palette` |
| `groups` | `title`, `palette`, and `rows`: `id`, `title`, `sub` |
| `cells` | `{(row id, column index): [(text, kind), ...]}` with `kind` `"role"`, `"read"` or `"write"` |

The failure patterns in [resilience.py](resilience.py) are a matrix, drawn with larger columns and boxes: a matrix spec may set `col_w` and `box_h` and its own `legend` when its boxes say more.

## BPMN

The TMS process models in [docs/processes/tms](../../processes/tms/) are BPMN files, which stay the source: they open in any BPMN tool and carry their own layout. [bpmn.py](bpmn.py) draws each one in this style so a process page can show it: a person's task is yellow, a system's task takes its lane's colour, events are rings, gateways are diamonds, and message flows are dashed. The layout in the file is kept.

```
python3 docs/architecture/diagrams/bpmn.py [process ...]   # writes bpmn-<process>.excalidraw and .png here
```

Change a process in its `.bpmn` and run the script. The ERP processes E1 and E2 have no model yet.

## Regenerating

```
python3 docs/architecture/diagrams/<spec>.py /tmp/preview.svg        # writes the .excalidraw next to the spec, in this folder
rsvg-convert -w 2966 /tmp/preview.svg -o docs/architecture/diagrams/<name>.png  # the PNG a Markdown page embeds
```

The PNG is rendered from the generator's SVG, not exported from Excalidraw, so its font differs slightly from the `.excalidraw` file's. Commit both.

## When you add a diagram

Terms in titles come from [GLOSSARY.md](../../../GLOSSARY.md). Put the new spec in this folder, write the `.excalidraw` and the `.png` beside it, link both from the page that explains the diagram, and add a row to [the architecture README](../README.md).

## Graph style

For the C4 diagrams in [c4.md](../c4.md), from [c4.py](c4.py), and for the context map, the TMS hexagon and the state diagrams, from [bounded_contexts.py](bounded_contexts.py), one spec per diagram. It looks like the matrix style (same colours, boxes, type, arrows, legend), but nodes sit on a grid and groups draw boundaries instead of lanes.

```
python3 docs/architecture/diagrams/c4.py [name ...]                # writes docs/architecture/diagrams/c4-<name>.excalidraw and .png; no names draws all
python3 docs/architecture/diagrams/bounded_contexts.py [name ...]  # writes docs/architecture/diagrams/<name>.excalidraw and .png
python3 docs/architecture/diagrams/order_lifecycle.py [name ...]   # writes docs/architecture/diagrams/order-lifecycle-<name>.excalidraw and .png
python3 docs/architecture/diagrams/deployment.py [name ...]        # writes the deployment view and the local-versus-Azure map
python3 docs/architecture/diagrams/message_trails.py [name ...]    # writes the Message trails of the process docs
python3 docs/architecture/diagrams/access.py                       # writes the identity and access grid
python3 docs/architecture/diagrams/resilience.py                   # writes the failure patterns
python3 docs/architecture/diagrams/bpmn.py [process ...]           # writes the BPMN pictures
```

| Key | Holds |
|---|---|
| `title`, `subtitle`, `footnote`, `legend`, `cell` | Header, legend entries, and the grid cell size in px (`cell=(width, height)`) |
| `nodes` | `id`, `col`, `row` (floats are fine), `title`, optional `sub` (use `\n` for lines), `palette` (a `PALETTE` key), `kind` (`auto`, `person` or `planned`), `w` |
| `groups` | A boundary around `members` (nodes, or other groups): `id`, `title`, `palette`, optional `dashed` |
| `edges` | `(from, to, label, options)` |

- **Colour is the application**, not the node's role: violet for the integration layer, blue for the TMS, and so on, as in the table above. A person is yellow, a planned system grey and dashed.
- **Titles** are the container's name, **subtitles** its technology in square brackets and then what it does.
- **The router** takes arrows orthogonally round boxes, spreads the arrows that share a side of a box, keeps parallel runs apart and puts each label where it covers no box. Leave it to it; lay the grid out so most arrows join neighbours.
- **Edge options**: `dashed` (planned), `plain` (no arrowhead), `both`, `orient` (`"h"` or `"v"`, to force the side an arrow leaves), `sides` (`("r", "t")`, the side it leaves and enters), and `via` (`"top"` or `"bottom"`) to run an arrow out of a box's side and round the outside of the drawing, for a return flow that would otherwise cross everything.
- Keep a group's members in neighbouring cells and leave about half a row between groups, or their boundaries touch.
- **A hexagon** draws the layers as nested groups, not a literal hexagon: driving adapters on the left, the Application (use cases and ports) with the Domain inside it, and the driven adapters on the right, with the outside world beyond them. Colour here is the **layer**, not the application, since the whole diagram is one application: orange for the driving adapters, teal for the Application, blue for the Domain, violet for Infrastructure, grey for what is outside, yellow for a person. Say so in the legend.
- **A state diagram** is one group per aggregate or dimension, with a dashed "Start" node. States are blue, an end state where the thing was carried out is green, an end state where nothing was carried out is grey, and a state that needs attention is orange. Label each arrow with the domain method that makes the move, and keep the guard in a table beside the picture, not in the label.
- Preview the SVG while you work: `c4.py` writes it to `/tmp/c4-<name>.svg`, and `rsvg-convert` makes the PNG.

The PNGs are rendered from the generator's SVG, not exported from Excalidraw, so their font differs slightly from the `.excalidraw` files'. Commit both.
