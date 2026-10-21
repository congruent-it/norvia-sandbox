#!/usr/bin/env python3
"""Norvia's Excalidraw sequence style: participants across the top, time running down, one arrow per message.

Same palette, type and look as the matrix and graph styles; see README.md. Usage from a spec file:

    from excalidraw_sequence import render_all
    render_all(SPECS, out_dir, prefix, names)    # writes .excalidraw and .png

A spec holds title, subtitle, footnote, legend (optional), participants and steps:

    participants: dict(id, title, sub, palette, kind)       kind "person" draws a person in yellow
    steps:  ("phase", "title")
            ("msg", from_id, to_id, "label", opts)           opts: dashed (a reply or an event), both
            ("self", id, "label")                            something the participant does on its own
            ("note", [ids], "text")                          a note spanning the participants it names
"""
import subprocess
from pathlib import Path

from excalidraw_canvas import ARROW, ARROW_PLANNED, CHAR, INK, MUTED, SOFT, Canvas, text_height, wrap
from excalidraw_matrix import PALETTE, PERSON

M, TOP = 40, 160
COL_W = 200
HEAD_W, HEAD_H = 168, 66
GAP = 20


def render(spec, out_excalidraw, out_svg=None):
    cv = Canvas()
    text, rect = cv.text, cv.rect
    parts = spec["participants"]
    n = len(parts)
    xs = {p["id"]: M + COL_W / 2 + i * COL_W for i, p in enumerate(parts)}
    width = M * 2 + n * COL_W

    # first pass: heights
    y = TOP + HEAD_H + 24
    rows = []
    for step in spec["steps"]:
        kind = step[0]
        if kind == "phase":
            h, gap = 30, 20
        elif kind == "msg":
            span = abs(xs[step[1]] - xs[step[2]]) - 20
            lines = wrap(step[3], 12, max(span, 120))
            h, gap = 18 + len(lines) * 15, 18
        elif kind == "self":
            lines = wrap(step[2], 12, COL_W * 1.6)
            h, gap = max(34, 14 + len(lines) * 15), 16
        else:  # note
            span = abs(xs[step[1][0]] - xs[step[1][-1]]) + HEAD_W - 20
            lines = wrap(step[2], 12, max(span, 160))
            h, gap = 18 + len(lines) * 15, 18
        rows.append((y, h))
        y += h + gap
    height = y + 70

    text(M, 30, width - 2 * M, spec["title"], 38, INK, bold=True)
    text(M, 82, width - 2 * M, spec["subtitle"], 16, MUTED, wrap_w=width - 2 * M)
    lx = M
    for lab, fill, stroke, dashed in spec["legend"]:
        rect(lx, 128, 26, 18, fill, stroke, 2, dashed)
        text(lx + 34, 129, len(lab) * 8.6 + 6, lab, 14, INK)
        lx += 34 + len(lab) * 8.6 + 40

    # lifelines first, so the rest sits on top
    for p in parts:
        x = xs[p["id"]]
        cv.arrow([(x, TOP + HEAD_H), (x, height - 60)], "#ced4da", True, False, None, None, plain=True)
    for p in parts:
        pal = PALETTE[p.get("palette", "gray")]
        fill, stroke = (PERSON if p.get("kind") == "person" else (pal[2], pal[3]))
        x = xs[p["id"]]
        rect(x - HEAD_W / 2, TOP, HEAD_W, HEAD_H, fill, stroke, 2)
        th = text(x - HEAD_W / 2 + 10, TOP + 10, HEAD_W - 20, p["title"], 14, INK, "center", wrap_w=HEAD_W - 24, bold=True)
        if p.get("sub"):
            text(x - HEAD_W / 2 + 10, TOP + 10 + th + 3, HEAD_W - 20, p["sub"], 11, SOFT, "center", wrap_w=HEAD_W - 20)

    for step, (ry, h) in zip(spec["steps"], rows):
        kind = step[0]
        if kind == "phase":
            rect(M, ry, width - 2 * M, h, "#f1f3f5", "#dee2e6", 1)
            text(M + 14, ry + 6, width - 2 * M - 28, step[1], 14, INK, bold=True)
        elif kind == "msg":
            _, a, b, label, *o = step
            opts = o[0] if o else {}
            xa, xb = xs[a], xs[b]
            dashed = opts.get("dashed", False)
            lines = wrap(label, 12, max(abs(xa - xb) - 20, 120))
            ay = ry + h - 8
            direction = 1 if xb > xa else -1
            cv.arrow([(xa + 4 * direction, ay), (xb - 4 * direction, ay)], ARROW_PLANNED if dashed else ARROW, dashed, opts.get("both", False), None, None)
            lw = max(len(l) for l in lines) * 12 * CHAR
            rect((xa + xb) / 2 - lw / 2 - 6, ry + 1, lw + 12, len(lines) * 15 + 2, "#ffffff", "#ffffff", 0, radius=4)
            text((xa + xb) / 2 - lw / 2 - 6, ry + 2, lw + 12, "\n".join(lines), 12, INK, "center")
        elif kind == "self":
            _, a, label = step
            x = xs[a]
            cv.arrow([(x + 4, ry + 8), (x + 34, ry + 8), (x + 34, ry + h - 6), (x + 4, ry + h - 6)], ARROW, False, False, None, None)
            sl = wrap(label, 12, COL_W * 1.6)
            rect(x + 40, ry + 2, max(len(l) for l in sl) * 12 * CHAR + 12, len(sl) * 15 + 4, "#ffffff", "#ffffff", 0, radius=4)
            text(x + 44, ry + 4, COL_W * 1.6, label, 12, INK, wrap_w=COL_W * 1.6)
        else:
            _, ids, label = step
            x1, x2 = min(xs[i] for i in ids) - HEAD_W / 2, max(xs[i] for i in ids) + HEAD_W / 2
            rect(x1, ry, x2 - x1, h, "#fff9db", "#f59f00", 1)
            text(x1 + 8, ry + 8, x2 - x1 - 16, label, 12, INK, "center", wrap_w=x2 - x1 - 16)

    text(M, height - 56, width - 2 * M, spec["footnote"], 13, MUTED, wrap_w=width - 2 * M)
    cv.save(width, height, out_excalidraw, out_svg)


def render_all(specs, out_dir, prefix, names=None, preview_dir="/tmp"):
    for name in names or specs:
        out = Path(out_dir) / f"{prefix}{name}.excalidraw"
        svg = f"{preview_dir}/{prefix}{name}.svg"
        render(specs[name], out, svg)
        try:
            subprocess.run(["rsvg-convert", "-z", "1", svg, "-o", str(out.with_suffix(".png"))], check=True)
        except FileNotFoundError:
            print("rsvg-convert not found: wrote the .excalidraw only")
        print("wrote", out.name)
