# Diagram style: Excalidraw matrix

How to draw a diagram that maps **process steps against applications**, in the same look as [business-automation-model.excalidraw](../business-automation-model.excalidraw). Open the `.excalidraw` file at [excalidraw.com](https://excalidraw.com) or with the VS Code Excalidraw extension. For system context and container diagrams keep using the Mermaid C4 style in [c4.md](../c4.md); this style is for matrices.

Diagrams are generated from a spec, not drawn by hand, so a process change is an edit to a list and a re-run. Hand edits to the `.excalidraw` file are lost on the next run.

| File | Is |
|---|---|
| [excalidraw_matrix.py](excalidraw_matrix.py) | The style: palette, geometry, routing. `render(spec, out.excalidraw, out.svg)` |
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

## Regenerating

```
python3 docs/architecture/diagrams/<spec>.py /tmp/preview.svg        # writes the .excalidraw next to the spec's page
rsvg-convert -w 2966 /tmp/preview.svg -o docs/architecture/<name>.png  # the PNG a Markdown page embeds
```

The PNG is rendered from the generator's SVG, not exported from Excalidraw, so its font differs slightly from the `.excalidraw` file's. Commit both.

## When you add a diagram

Terms in titles come from [GLOSSARY.md](../../../GLOSSARY.md). Put the new spec in this folder, write the `.excalidraw` next to the other architecture pages, and add a row to [the architecture README](../README.md).
