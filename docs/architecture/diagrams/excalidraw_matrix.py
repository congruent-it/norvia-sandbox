#!/usr/bin/env python3
"""Norvia's Excalidraw matrix style: rows are applications, columns are process steps.

Usage from a spec file:

    from excalidraw_matrix import render, PALETTE, DEFAULT_LEGEND
    render(SPEC, "out.excalidraw", "preview.svg")   # the SVG preview is optional

The style is described in README.md. SPEC is documented there too; business-automation-model.py is the
reference spec. The .excalidraw file is the deliverable, the SVG is only a quick way to check the layout
(`rsvg-convert -w 2600 preview.svg -o preview.png`).
"""
import json
import random
from xml.sax.saxutils import escape

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
INK, MUTED, SOFT, ARROW, ARROW_PLANNED = "#1e1e1e", "#6b7280", "#5c6370", "#495057", "#868e96"
FONT = 6  # Excalidraw's Nunito
LH = 1.25
CHAR = 0.54  # average glyph width / font size, used to wrap text conservatively

# ---------- geometry ----------
M, LABEL_W, COL_W, BOX_W, BOX_H, GAP, PAD, LANE_GAP = 40, 210, 244, 208, 94, 16, 16, 8
GX0 = M + LABEL_W + 12
CROSS_H = 92


def render(spec, out_excalidraw, out_svg=None):
    rnd = random.Random(7)
    els, svg, counter = [], [], [0]

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

    # primitives
    def base(t, x, y, w, h, **kw):
        counter[0] += 1
        e = dict(id=kw.pop("id", None) or f"{t[:3]}{counter[0]:04d}", type=t, x=x, y=y, width=w, height=h, angle=0,
                 strokeColor=INK, backgroundColor="transparent", fillStyle="solid", strokeWidth=1,
                 strokeStyle="solid", roughness=0, opacity=100, groupIds=[], frameId=None, roundness=None,
                 seed=rnd.randint(1, 2**31 - 1), version=1, versionNonce=rnd.randint(1, 2**31 - 1),
                 isDeleted=False, boundElements=[], updated=1760000000000, link=None, locked=False)
        e.update(kw)
        return e

    def rect(x, y, w, h, fill, stroke, sw=1, dashed=False, groups=None, id=None):
        e = base("rectangle", x, y, w, h, backgroundColor=fill, strokeColor=stroke, strokeWidth=sw,
                 strokeStyle="dashed" if dashed else "solid", groupIds=groups or [], id=id, roundness={"type": 3})
        els.append(e)
        svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{fill}" stroke="{stroke}" '
                   f'stroke-width="{sw}"' + (' stroke-dasharray="7 5"' if dashed else '') + '/>')
        return e

    def wrap(s, fs, width):
        out = []
        for para in s.split("\n"):
            line = ""
            for w in para.split(" "):
                t = (line + " " + w).strip()
                if len(t) * fs * CHAR <= width or not line:
                    line = t
                else:
                    out.append(line)
                    line = w
            out.append(line)
        return out

    def text(x, y, w, s, fs=15, color=INK, align="left", groups=None, wrap_w=None, bold=False):
        lines = wrap(s, fs, wrap_w or 10**6)
        body = "\n".join(lines)
        h = len(lines) * fs * LH
        els.append(base("text", x, y, w, h, strokeColor=color, groupIds=groups or [], text=body, fontSize=fs,
                        fontFamily=FONT, textAlign=align, verticalAlign="top", baseline=int(fs * 0.9),
                        containerId=None, originalText=body, autoResize=False, lineHeight=LH))
        anchor = {"left": "start", "center": "middle", "right": "end"}[align]
        tx = x if align == "left" else (x + w / 2 if align == "center" else x + w)
        for i, ln in enumerate(lines):
            svg.append(f'<text x="{tx}" y="{y + i * fs * LH + fs * 0.92}" font-size="{fs}" fill="{color}" '
                       f'text-anchor="{anchor}" font-family="Nunito, Helvetica, Arial, sans-serif"'
                       + (' font-weight="700"' if bold else '') + f'>{escape(ln)}</text>')
        return h

    def arrow(pts, color, dashed, both, start, end):
        x0, y0 = pts[0]
        rel = [[px - x0, py - y0] for px, py in pts]
        w = max(p[0] for p in rel) - min(p[0] for p in rel)
        h = max(p[1] for p in rel) - min(p[1] for p in rel)
        e = base("arrow", x0, y0, w, h, strokeColor=color, strokeWidth=2, strokeStyle="dashed" if dashed else "solid",
                 points=rel, lastCommittedPoint=None,
                 startBinding={"elementId": start, "focus": 0, "gap": 4},
                 endBinding={"elementId": end, "focus": 0, "gap": 4},
                 startArrowhead="arrow" if both else None, endArrowhead="arrow", elbowed=False)
        els.append(e)
        svg.append(f'<path d="M {" L ".join(f"{px} {py}" for px, py in pts)}" fill="none" stroke="{color}" '
                   f'stroke-width="2" stroke-linejoin="round" marker-end="url(#ah)"'
                   + (' marker-start="url(#ah)"' if both else '') + (' stroke-dasharray="7 5"' if dashed else '') + '/>')
        return e

    svg.append(f'<rect width="{total_w}" height="{total_h}" fill="#ffffff"/>')

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

    with open(out_excalidraw, "w") as f:
        json.dump(dict(type="excalidraw", version=2, source="https://excalidraw.com", elements=els,
                       appState=dict(gridSize=None, viewBackgroundColor="#ffffff"), files={}), f, indent=2)
    if out_svg:
        head = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{total_w}" height="{total_h}" viewBox="0 0 {total_w} {total_h}">'
                '<defs><marker id="ah" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
                '<path d="M 0 1 L 9 5 L 0 9" fill="none" stroke="context-stroke" stroke-width="1.6"/></marker></defs>')
        with open(out_svg, "w") as f:
            f.write(head + "".join(svg) + "</svg>")
