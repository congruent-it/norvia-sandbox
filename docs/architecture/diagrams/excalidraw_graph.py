#!/usr/bin/env python3
"""Norvia's Excalidraw graph style: boxes in a grid, boundaries around them, orthogonal arrows.

For system context, container and component diagrams (the C4 levels). Same palette, type and look as the
matrix style; see README.md. Usage from a spec file:

    from excalidraw_graph import render
    render(SPEC, "out.excalidraw", "preview.svg")   # the SVG preview is optional

Nodes sit at (col, row) positions on a grid, the arrows are routed around them for you, and groups draw a
boundary around their members (nodes or other groups).
"""
import subprocess
from pathlib import Path

from excalidraw_canvas import ARROW, ARROW_PLANNED, CHAR, INK, MUTED, SOFT, Canvas, text_height, wrap
from excalidraw_matrix import DEFAULT_LEGEND, PALETTE, PERSON, PLANNED

M, TOP = 40, 160
NODE_W, NODE_MIN_H = 232, 64
PAD, PAD_TOP = 22, 48
CLEAR = 6  # how far arrows keep from a box they pass


def render(spec, out_excalidraw, out_svg=None):
    cv = Canvas()
    cw, rh = spec.get("cell", (300, 140))
    nodes = {n["id"]: dict(n) for n in spec["nodes"]}

    # node geometry
    for n in nodes.values():
        n["w"] = n.get("w", NODE_W)
        sub = n.get("sub", "")
        th = text_height(n["title"], 15, n["w"] - 30)
        sh = text_height(sub, 12, n["w"] - 24) if sub else 0
        n["h"] = max(NODE_MIN_H, 11 + th + (4 if sub else 0) + sh + 11)
        n["cx"], n["cy"] = (n["col"] + 0.5) * cw, (n["row"] + 0.5) * rh
        n["x"], n["y"] = n["cx"] - n["w"] / 2, n["cy"] - n["h"] / 2

    # group boundaries, innermost first
    groups = {g["id"]: dict(g) for g in spec.get("groups", [])}

    def bounds(gid):
        g = groups[gid]
        if "rect" in g:
            return g["rect"]
        xs, ys, xe, ye = [], [], [], []
        for m in g["members"]:
            x, y, w, h = bounds(m) if m in groups else (nodes[m]["x"], nodes[m]["y"], nodes[m]["w"], nodes[m]["h"])
            xs.append(x); ys.append(y); xe.append(x + w); ye.append(y + h)
        g["rect"] = (min(xs) - PAD, min(ys) - PAD_TOP, max(xe) - min(xs) + 2 * PAD, max(ye) - min(ys) + PAD + PAD_TOP)
        return g["rect"]

    for gid in groups:
        bounds(gid)

    # translate everything so the drawing starts at the margin
    allr = [(n["x"], n["y"], n["w"], n["h"]) for n in nodes.values()] + [g["rect"] for g in groups.values()]
    dx0, dy0 = M - min(r[0] for r in allr), TOP - min(r[1] for r in allr)
    for n in nodes.values():
        n["x"] += dx0; n["y"] += dy0; n["cx"] += dx0; n["cy"] += dy0
    for g in groups.values():
        x, y, w, h = g["rect"]
        g["rect"] = (x + dx0, y + dy0, w, h)
    width = max(x + w for x, y, w, h in [(n["x"], n["y"], n["w"], n["h"]) for n in nodes.values()] + [g["rect"] for g in groups.values()]) + M
    height = max(y + h for x, y, w, h in [(n["x"], n["y"], n["w"], n["h"]) for n in nodes.values()] + [g["rect"] for g in groups.values()]) + 90

    # ----- routing -----
    edges = [dict(src=e[0], dst=e[1], label=e[2] if len(e) > 2 else "", **(e[3] if len(e) > 3 else {})) for e in spec["edges"]]
    for e in edges:
        a, b = nodes[e["src"]], nodes[e["dst"]]
        ddx, ddy = b["cx"] - a["cx"], b["cy"] - a["cy"]
        orient = e.get("orient") or ("v" if abs(ddx) < (a["w"] + b["w"]) / 2 * 0.55 else "h")
        e["orient"] = orient
        if "sides" in e:
            e["ss"], e["ds"] = e["sides"]
        elif orient == "h":
            e["ss"], e["ds"] = ("r", "l") if ddx > 0 else ("l", "r")
        else:
            e["ss"], e["ds"] = ("b", "t") if ddy > 0 else ("t", "b")

    # spread the arrows that share one side of a box along that side
    ports = {}
    for i, e in enumerate(edges):
        ports.setdefault((e["src"], e["ss"]), []).append((i, "s", e["dst"]))
        ports.setdefault((e["dst"], e["ds"]), []).append((i, "d", e["src"]))
    for (nid, side), lst in ports.items():
        n = nodes[nid]
        key = (lambda t: nodes[t[2]]["cy"]) if side in "lr" else (lambda t: nodes[t[2]]["cx"])
        lst.sort(key=key)
        for k, (i, end, _) in enumerate(lst):
            f = (k + 1) / (len(lst) + 1)
            if side in "lr":
                p = (n["x"] + (n["w"] if side == "r" else 0), n["y"] + n["h"] * f)
            else:
                p = (n["x"] + n["w"] * f, n["y"] + (n["h"] if side == "b" else 0))
            edges[i]["p" + end] = p

    obstacles = {nid: (n["x"] - CLEAR, n["y"] - CLEAR, n["x"] + n["w"] + CLEAR, n["y"] + n["h"] + CLEAR) for nid, n in nodes.items()}
    placed = []  # (edge index, segments)

    def seg_hits(seg, rect):
        (x1, y1), (x2, y2) = seg
        rx1, ry1, rx2, ry2 = rect
        if abs(x1 - x2) < 0.01:
            return rx1 < x1 < rx2 and min(y1, y2) < ry2 and max(y1, y2) > ry1
        return ry1 < y1 < ry2 and min(x1, x2) < rx2 and max(x1, x2) > rx1

    def cost(path, ei, skip):
        segs = list(zip(path, path[1:]))
        c = 0
        for seg in segs:
            for nid, r in obstacles.items():
                if nid not in skip and seg_hits(seg, r):
                    c += 10000
            for oi, osegs in placed:
                for o in osegs:
                    (x1, y1), (x2, y2) = seg
                    (u1, v1), (u2, v2) = o
                    if abs(x1 - x2) < 0.01 and abs(u1 - u2) < 0.01 and abs(x1 - u1) < 12:
                        if min(y1, y2) < max(v1, v2) - 4 and max(y1, y2) > min(v1, v2) + 4:
                            c += 250
                    elif abs(y1 - y2) < 0.01 and abs(v1 - v2) < 0.01 and abs(y1 - v1) < 12:
                        if min(x1, x2) < max(u1, u2) - 4 and max(x1, x2) > min(u1, u2) + 4:
                            c += 250
                    elif abs(x1 - x2) < 0.01 and abs(v1 - v2) < 0.01:
                        if min(u1, u2) < x1 < max(u1, u2) and min(y1, y2) < v1 < max(y1, y2):
                            c += 12
                    elif abs(y1 - y2) < 0.01 and abs(u1 - u2) < 0.01:
                        if min(v1, v2) < y1 < max(v1, v2) and min(x1, x2) < u1 < max(x1, x2):
                            c += 12
        c += sum(abs(a[0] - b[0]) + abs(a[1] - b[1]) for a, b in segs) * 0.01 + len(segs) * 6
        return c

    def route(e, ei):
        s, d = e["ps"], e["pd"]
        skip = {e["src"], e["dst"]}
        horiz = e["orient"] == "h"
        if e.get("via"):
            # loop round the outside of the drawing: out of the side, along a corridor above or below every box, then in
            bottom = e["via"] == "bottom"
            ys = [y2 for (_, _, _, y2) in obstacles.values()] if bottom else [y1 for (_, y1, _, _) in obstacles.values()]
            edge_y = max(ys) if bottom else min(ys)
            sgn_x = 1 if e["ss"] == "r" else -1
            best = None
            for dy in range(14, 200, 6):
                Y = edge_y + dy if bottom else edge_y - dy
                for dx in range(14, 90, 6):
                    gx = s[0] + sgn_x * dx
                    path = [s, (gx, s[1]), (gx, Y), (d[0], Y), d]
                    k = cost(path, ei, skip)
                    if best is None or k < best[0]:
                        best = (k, path)
            return best[1]
        T = (lambda p: p) if horiz else (lambda p: (p[1], p[0]))  # transpose so one routine serves both
        ts, td = T(s), T(d)
        back = lambda pts: [T(p) for p in pts]  # transpose is its own inverse
        cands = []
        if abs(ts[1] - td[1]) < 1:
            cands.append([ts, td])
        lo, hi = sorted((ts[0], td[0]))
        step = 6
        x = lo + 10
        sgn = 1 if td[0] > ts[0] else -1
        while x < hi - 10:
            cands.append([ts, (x, ts[1]), (x, td[1]), td])
            x += step
        # detour: leave, run along a corridor, come back (for a box in the way)
        ylo = min(ts[1], td[1]) - 260
        yhi = max(ts[1], td[1]) + 260
        m1, m2 = ts[0] + sgn * 14, td[0] - sgn * 14
        y = ylo
        while y <= yhi:
            cands.append([ts, (m1, ts[1]), (m1, y), (m2, y), (m2, td[1]), td])
            y += 8
        # near-end jogs: vertical run right after the source or right before the target
        best = None
        for c in cands:
            pts = back(c)
            k = cost(pts, ei, skip) + (60 if len(c) == 6 else 0)
            if best is None or k < best[0]:
                best = (k, pts)
        return best[1]

    # route the shorter arrows first, so long ones go around them
    order = sorted(range(len(edges)), key=lambda i: abs(edges[i]["ps"][0] - edges[i]["pd"][0]) + abs(edges[i]["ps"][1] - edges[i]["pd"][1]))
    for i in order:
        edges[i]["path"] = route(edges[i], i)
        placed.append((i, list(zip(edges[i]["path"], edges[i]["path"][1:]))))

    # corridors can run outside the boxes: make room for them above the title line and below the footer
    ys = [p[1] for e in edges for p in e["path"]]
    xs = [p[0] for e in edges for p in e["path"]]
    shift = max(0, TOP - 10 - min(ys))
    if shift:
        for n in nodes.values():
            n["y"] += shift; n["cy"] += shift
        for g in groups.values():
            x, y, w, h = g["rect"]
            g["rect"] = (x, y + shift, w, h)
        for e in edges:
            e["path"] = [(px, py + shift) for px, py in e["path"]]
    width = max(width, max(xs) + M)
    height = max(height, max(p[1] for e in edges for p in e["path"]) + 90)

    # ----- drawing -----
    text, rect = cv.text, cv.rect
    text(M, 30, 1600, spec["title"], 38, INK, bold=True)
    text(M, 82, 1600, spec["subtitle"], 16, MUTED)
    legend = spec.get("legend", DEFAULT_LEGEND)
    lx = M
    for lab, fill, stroke, dashed in legend:
        rect(lx, 112, 26, 18, fill, stroke, 2, dashed)
        text(lx + 34, 113, len(lab) * 8.6 + 6, lab, 14, INK)
        lx += 34 + len(lab) * 8.6 + 40

    for g in sorted(groups.values(), key=lambda g: -g["rect"][2] * g["rect"][3]):
        pal = PALETTE[g.get("palette", "gray")]
        x, y, w, h = g["rect"]
        rect(x, y, w, h, pal[1], pal[3], 1.5, g.get("dashed", False), radius=14)
        text(x + 16, y + 12, w - 32, g["title"], 14, pal[0], bold=True)
        if g.get("sub"):
            text(x + 16, y + 30, w - 32, g["sub"], 12, MUTED)

    for n in nodes.values():
        pal = PALETTE[n.get("palette", "blue")]
        kind = n.get("kind", "auto")
        fill, stroke, dashed = {"person": (*PERSON, False), "planned": (*PLANNED, True)}.get(kind, (pal[2], pal[3], False))
        gid = [f"grp-{n['id']}"]
        n["el"] = rect(n["x"], n["y"], n["w"], n["h"], fill, stroke, 2, dashed, gid, f"box-{n['id']}")
        th = text(n["x"] + 12, n["y"] + 11, n["w"] - 24, n["title"], 15, MUTED if kind == "planned" else INK, "center", gid, n["w"] - 30, True)
        if n.get("sub"):
            text(n["x"] + 12, n["y"] + 11 + th + 4, n["w"] - 24, n["sub"], 12, SOFT, "center", gid, n["w"] - 24)

    for e in edges:
        dashed = e.get("dashed", False)
        arr = cv.arrow(e["path"], ARROW_PLANNED if dashed else ARROW, dashed, e.get("both", False), f"box-{e['src']}", f"box-{e['dst']}", e.get("plain", False))
        for nid in (e["src"], e["dst"]):
            nodes[nid]["el"]["boundElements"].append({"id": arr["id"], "type": "arrow"})
    label_rects = []
    node_rects = [(n["x"], n["y"], n["x"] + n["w"], n["y"] + n["h"]) for n in nodes.values()]
    node_rects += [(g["rect"][0], g["rect"][1], g["rect"][0] + g["rect"][2], g["rect"][1] + 46) for g in groups.values()]
    overlap = lambda r, q: r[0] < q[2] and r[2] > q[0] and r[1] < q[3] and r[3] > q[1]
    for e in sorted(edges, key=lambda e: len(e["label"])):  # labels last, so they sit on top of the lines
        if not e["label"]:
            continue
        lines = wrap(e["label"], 12, 170)
        lw = max(len(l) for l in lines) * 12 * CHAR + 12
        lh = len(lines) * 12 * 1.25
        segs = sorted(zip(e["path"], e["path"][1:]), key=lambda s: -(abs(s[0][0] - s[1][0]) + abs(s[0][1] - s[1][1])))
        spots = [((a[0] + (b[0] - a[0]) * f), (a[1] + (b[1] - a[1]) * f)) for a, b in segs for f in (0.5, 0.3, 0.7, 0.15, 0.85)]
        chosen = spots[0]
        for mx, my in spots:
            r = (mx - lw / 2 - 12, my - lh / 2 - 6, mx + lw / 2 + 12, my + lh / 2 + 6)
            if not any(overlap(r, q) for q in node_rects + label_rects):
                chosen = (mx, my)
                break
        mx, my = chosen
        label_rects.append((mx - lw / 2 - 12, my - lh / 2 - 6, mx + lw / 2 + 12, my + lh / 2 + 6))
        rect(mx - lw / 2, my - lh / 2 - 2, lw, lh + 4, "#ffffff", "#ffffff", 0, radius=4)
        text(mx - lw / 2, my - lh / 2, lw, "\n".join(lines), 12, ARROW, "center")

    text(M, height - 60, width - 2 * M, spec["footnote"], 13, MUTED, wrap_w=width - 2 * M)
    cv.save(width, height, out_excalidraw, out_svg)


def render_all(specs, out_dir, prefix, names=None, preview_dir="/tmp"):
    """Draw each spec to <out_dir>/<prefix><name>.excalidraw and .png (the PNG needs rsvg-convert)."""
    for name in names or specs:
        out = Path(out_dir) / f"{prefix}{name}.excalidraw"
        svg = f"{preview_dir}/{prefix}{name}.svg"
        render(specs[name], out, svg)
        try:
            subprocess.run(["rsvg-convert", "-z", "1", svg, "-o", str(out.with_suffix(".png"))], check=True)
        except FileNotFoundError:
            print("rsvg-convert not found: wrote the .excalidraw only")
        print("wrote", out.name)
