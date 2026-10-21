#!/usr/bin/env python3
"""Norvia's Excalidraw grid style: who has what, as rows against columns.

Same palette, type and look as the other styles; see README.md. Usage from a spec file:

    from excalidraw_grid import render_all
    render_all(SPECS, out_dir, prefix, names)    # writes .excalidraw and .png

A spec holds title, subtitle, footnote, legend (optional), and:

    columns: [dict(title, sub, palette)]
    groups:  [dict(title, palette, rows=[dict(id, title, sub)])]
    cells:   {(row id, column index): [(text, kind), ...]}      kind: a key of KINDS, or a (fill, stroke) pair
"""
import subprocess
from pathlib import Path

from excalidraw_canvas import CHAR, INK, MUTED, SOFT, Canvas, text_height, wrap
from excalidraw_matrix import PALETTE

M, TOP = 40, 160
LABEL_W, COL_W = 210, 232
CHIP_PAD, CHIP_GAP, ROW_PAD = 5, 5, 8
KINDS = {  # what a chip says about the row's access to the column
    "role": ("#d0ebff", "#1c7ed6"),    # holds an API role: may call
    "read": ("#d3f9d8", "#2f9e44"),    # receives, reads, lists
    "write": ("#ffe8cc", "#e8590c"),   # sends, writes, resubmits
}


def render(spec, out_excalidraw, out_svg=None):
    cv = Canvas()
    text, rect = cv.text, cv.rect
    cols = spec["columns"]
    n = len(cols)
    width = M * 2 + LABEL_W + n * COL_W
    cells = spec["cells"]

    def chip_h(t):
        return text_height(t, 11, COL_W - 12 - 16) + 2 * CHIP_PAD

    rows = [(g, r) for g in spec["groups"] for r in g["rows"]]
    heights = {}
    for _, r in rows:
        col_h = [sum(chip_h(t) + CHIP_GAP for t, _ in cells.get((r["id"], c), [])) for c in range(n)]
        heights[r["id"]] = max(42, max(col_h) + ROW_PAD * 2 - CHIP_GAP)

    y = TOP + 80
    total_h = y + sum(h + 6 for h in heights.values()) + len(spec["groups"]) * 42 + 80
    text(M, 30, width - 2 * M, spec["title"], 38, INK, bold=True)
    sub_h = text(M, 82, width - 2 * M, spec["subtitle"], 16, MUTED, wrap_w=width - 2 * M)
    lx, ly = M, int(82 + sub_h + 10)
    for lab, kind in spec["legend"]:
        fill, stroke = KINDS[kind] if isinstance(kind, str) else kind
        rect(lx, ly, 26, 18, fill, stroke, 2)
        text(lx + 34, ly + 1, len(lab) * 8.6 + 6, lab, 14, INK)
        lx += 34 + len(lab) * 8.6 + 40

    # column headers
    for c, col in enumerate(cols):
        pal = PALETTE[col.get("palette", "gray")]
        x = M + LABEL_W + c * COL_W + 4
        rect(x, TOP, COL_W - 8, 66, pal[0], pal[0], 1)
        th = text(x + 8, TOP + 8, COL_W - 24, col["title"], 15, "#ffffff", "center", wrap_w=COL_W - 28, bold=True)
        if col.get("sub"):
            text(x + 8, TOP + 8 + th + 2, COL_W - 24, col["sub"], 11, "#ffffff", "center", wrap_w=COL_W - 28)

    for g in spec["groups"]:
        pal = PALETTE[g.get("palette", "gray")]
        rect(M, y, width - 2 * M, 30, pal[1], pal[3], 1)
        text(M + 12, y + 6, width - 2 * M - 24, g["title"], 14, pal[0], bold=True)
        y += 36
        for r in g["rows"]:
            h = heights[r["id"]]
            rect(M, y, width - 2 * M, h, "#ffffff", "#e9ecef", 1, radius=6)
            rect(M, y, LABEL_W - 6, h, pal[2], pal[3], 1, radius=6)
            th = text(M + 10, y + 7, LABEL_W - 26, r["title"], 13, INK, wrap_w=LABEL_W - 28, bold=True)
            if r.get("sub"):
                text(M + 10, y + 7 + th + 1, LABEL_W - 26, r["sub"], 11, SOFT, wrap_w=LABEL_W - 28)
            for c in range(n):
                cy = y + ROW_PAD
                for t, kind in cells.get((r["id"], c), []):
                    fill, stroke = KINDS[kind] if isinstance(kind, str) else kind
                    ch = chip_h(t)
                    cx = M + LABEL_W + c * COL_W + 6
                    rect(cx, cy, COL_W - 12, ch, fill, stroke, 1, radius=6)
                    text(cx + 8, cy + CHIP_PAD, COL_W - 28, t, 11, INK, wrap_w=COL_W - 28)
                    cy += ch + CHIP_GAP
            y += h + 6
        y += 6
    text(M, total_h - 56, width - 2 * M, spec["footnote"], 13, MUTED, wrap_w=width - 2 * M)
    cv.save(width, total_h, out_excalidraw, out_svg)


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
