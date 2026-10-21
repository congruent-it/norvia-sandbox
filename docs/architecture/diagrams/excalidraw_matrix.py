#!/usr/bin/env python3
"""Norvia's Excalidraw matrix style: rows are applications, columns are process steps.

Usage from a spec file:

    from excalidraw_matrix import render, PALETTE, DEFAULT_LEGEND
    render(SPEC, "out.excalidraw", "preview.svg")   # the SVG preview is optional

The style is described in README.md. SPEC is documented there too; business-automation-model.py is the
reference spec. The .excalidraw file is the deliverable, the SVG is only a quick way to check the layout
(`rsvg-convert -w 2600 preview.svg -o preview.png`).
"""
from excalidraw_canvas import ARROW, ARROW_PLANNED, INK, MUTED, SOFT, Canvas

# ---------- style tokens ----------
# name: (lane label colour, lane tint, box fill, box stroke)
PALETTE = {
    "gray": ("#6b7280", "#f6f7f8", "#e9ecef", "#868e96"),      # outside Norvia
    "violet": ("#7048e8", "#f6f2ff", "#e5dbff", "#7048e8"),    # integration layer
    "blue": ("#1c7ed6", "#eff7ff", "#d0ebff", "#1c7ed6"),      # TMS
    "teal": ("#0c8599", "#effafc", "#c5f6fa", "#0c8599"),      # Driver app
    "green": ("#2f9e44", "#f1fbf3", "#d3f9d8", "#2f9e44"),     # ERP
    "pink": ("#d6336c", "#fff0f6", "#ffdeeb", "#d6336c"),      # Integration console
    "orange": ("#e8590c", "#fff4e6", "#ffe8cc", "#e8590c"),    # Simulator
    "lightgray": ("#adb5bd", "#f8f9fa", "#f1f3f5", "#adb5bd"), # planned (use with dashed=True)
}
STREAM_TINT = {"blue": "#e7f5ff", "green": "#ebfbee", "orange": "#fff4e6", "violet": "#f3f0ff", "pink": "#fff0f6"}
PERSON = ("#fff3bf", "#f59f00")     # a person acts
PLANNED = ("#f1f3f5", "#adb5bd")    # planned or reference data, always dashed
DEFAULT_LEGEND = [
    ("A person acts", *PERSON, False),
    ("The application decides", "#d0ebff", "#1c7ed6", False),
    ("An integration flow runs", "#e5dbff", "#7048e8", False),
    ("Planned, or reference data", *PLANNED, True),
]

# ---------- geometry ----------
M, LABEL_W, COL_W, BOX_W, BOX_H, GAP, PAD, LANE_GAP = 40, 210, 244, 208, 94, 16, 16, 8
GX0 = M + LABEL_W + 12
CROSS_H = 92


def render(spec, out_excalidraw, out_svg=None):

    lanes = {l["key"]: l for l in spec["lanes"]}
    cols = [c for s in spec["streams"] for c in s["cols"]]
    steps = {s[0]: s for s in spec["steps"]}

    # layout
    slots = {k: 1 for k in lanes}
    for _, _, lane, slot, *_ in spec["steps"]:
        slots[lane] = max(slots[lane], slot + 1)
    stream_y, stream_h, colh_y, colh_h = 130, 34, 170, 62
    y = colh_y + colh_h + 14
    lane_y, lane_h = {}, {}
    for l in spec["lanes"]:
        n = slots[l["key"]]
        lane_h[l["key"]] = 2 * PAD + n * BOX_H + (n - 1) * GAP
        lane_y[l["key"]] = y
        y += lane_h[l["key"]] + LANE_GAP
    cross_y = []
    y += 10
    for _ in spec.get("cross", []):
        cross_y.append(y)
        y += CROSS_H + LANE_GAP
    foot_y = y + 18
    right = GX0 + len(cols) * COL_W
    total_w, total_h = right + 20, foot_y + 50

    def box_rect(sid):
        _, c, lane, slot, *_ = steps[sid]
        return (GX0 + c * COL_W + (COL_W - BOX_W) / 2, lane_y[lane] + PAD + slot * (BOX_H + GAP), BOX_W, BOX_H)

    cv = Canvas()
    rect, text, arrow = cv.rect, cv.text, cv.arrow
    els = cv.els


    # title and legend
    text(M, 30, 1500, spec["title"], 38, INK, bold=True)
    text(M, 82, 1500, spec["subtitle"], 16, MUTED)
    legend = spec.get("legend", DEFAULT_LEGEND)
    lx = total_w - 20 - len(legend) * 196
    for i, (lab, fill, stroke, dashed) in enumerate(legend):
        rect(lx + i * 196, 44, 26, 18, fill, stroke, 2, dashed)
        text(lx + i * 196 + 34, 45, 156, lab, 14, INK)

    # value streams and column headers
    c0 = 0
    for st in spec["streams"]:
        colour = PALETTE[st["palette"]][0]
        tint = STREAM_TINT[st["palette"]]
        x0, w = GX0 + c0 * COL_W + 6, len(st["cols"]) * COL_W - 12
        rect(x0, stream_y, w, stream_h, colour, colour)
        text(x0, stream_y + 6, w, st["name"].upper(), 15, "#ffffff", "center", bold=True)
        for i, (name, sub) in enumerate(st["cols"]):
            cx = GX0 + (c0 + i) * COL_W + 6
            rect(cx, colh_y, COL_W - 12, colh_h, tint, colour, 2)
            text(cx, colh_y + 10, COL_W - 12, name, 18, INK, "center", bold=True)
            text(cx, colh_y + 36, COL_W - 12, sub, 13, MUTED, "center")
        c0 += len(st["cols"])

    # lanes and cross-cutting lanes
    def lane_frame(y0, h, name, sub, colour, tint, dashed=False):
        rect(M, y0, right - M, h, tint, "#e5e7eb", 1, dashed)
        rect(M, y0, LABEL_W, h, colour, colour, 1, dashed)
        text(M + 16, y0 + h / 2 - 28, LABEL_W - 32, name, 18 if len(name) > 17 else 21, "#ffffff", bold=True)
        text(M + 16, y0 + h / 2 + 2, LABEL_W - 32, sub, 13, "#ffffff")

    for l in spec["lanes"]:
        pal = PALETTE[l["palette"]]
        lane_frame(lane_y[l["key"]], lane_h[l["key"]], l["name"], l["sub"], pal[0], pal[1], l.get("dashed", False))
    for y0, c in zip(cross_y, spec.get("cross", [])):
        pal = PALETTE[c["palette"]]
        lane_frame(y0, CROSS_H, c["name"], c["sub"], pal[0], pal[1])
        bx, bw = GX0 + 8, right - GX0 - 20
        rect(bx, y0 + 14, bw, CROSS_H - 28, "#ffffff", pal[0], 2)
        text(bx + 20, y0 + 24, bw - 40, c["headline"], 17, INK, bold=True)
        text(bx + 20, y0 + 50, bw - 40, c["detail"], 14, MUTED)

    # step boxes
    box_el, bounds = {}, {}
    for sid, _, lane, _, kind, title, sub in spec["steps"]:
        x, yy, w, h = box_rect(sid)
        pal = PALETTE[lanes[lane]["palette"]]
        fill, stroke, dashed = {"person": (*PERSON, False), "planned": (*PLANNED, True)}.get(kind, (pal[2], pal[3], False))
        g = [f"grp-{sid}"]
        box_el[sid] = rect(x, yy, w, h, fill, stroke, 2, dashed, g, f"box-{sid}")
        bounds[sid] = (x, yy, w, h)
        th = text(x + 12, yy + 11, w - 24, title, 15, MUTED if kind == "planned" else INK, "center", g, w - 30, True)
        text(x + 12, yy + 11 + th + 4, w - 24, sub, 12, SOFT, "center", g, w - 24)

    # arrows
    def anchor(sid, side, dy=0):
        x, yy, w, h = bounds[sid]
        return {"r": (x + w, yy + h / 2 + dy), "l": (x, yy + h / 2 + dy), "t": (x + w / 2, yy), "b": (x + w / 2, yy + h)}[side]

    for e in spec["edges"]:
        src, dst, mode = e[:3]
        o = e[3] if len(e) > 3 else {}
        dashed, both = o.get("dashed", False), o.get("both", False)
        off, sdy, ddy = o.get("off", 9), o.get("sdy", 0), o.get("ddy", 0)
        if mode == "h":
            a, b = anchor(src, "r", sdy), anchor(dst, "l", ddy)
            pts = [a, b] if abs(a[1] - b[1]) < 1 else [a, (a[0] + 18, a[1]), (a[0] + 18, b[1]), b]
        elif mode == "v":
            down = bounds[dst][1] > bounds[src][1]
            pts = [anchor(src, "b" if down else "t"), anchor(dst, "t" if down else "b")]
        else:  # gr / gl: leave and enter on the same side, running in the gutter between columns
            side = "r" if mode == "gr" else "l"
            a, b = anchor(src, side, sdy), anchor(dst, side, ddy)
            gx = a[0] + off if mode == "gr" else a[0] - off
            pts = [a, (gx, a[1]), (gx, b[1]), b]
        arr = arrow(pts, ARROW_PLANNED if dashed else ARROW, dashed, both, f"box-{src}", f"box-{dst}")
        for sid in (src, dst):
            box_el[sid]["boundElements"].append({"id": arr["id"], "type": "arrow"})

    for t, sid, dx, dy in spec.get("edge_labels", []):
        x, yy, w, h = bounds[sid]
        text(x + dx, yy + h / 2 + dy, 70, t, 12, ARROW, "center", bold=True)

    text(M, foot_y, 2600, spec["footnote"], 13, MUTED)

    cv.save(total_w, total_h, out_excalidraw, out_svg)
