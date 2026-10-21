"""Drawing primitives shared by Norvia's Excalidraw diagram styles.

A Canvas collects Excalidraw elements and an SVG preview of them. See README.md for the style.
"""
import json
import random
import re
from xml.sax.saxutils import escape

INK, MUTED, SOFT, ARROW, ARROW_PLANNED = "#1e1e1e", "#6b7280", "#5c6370", "#495057", "#868e96"
FONT = 6  # Excalidraw's Nunito
LH = 1.25
CHAR = 0.57  # average glyph width / font size, used to wrap text conservatively


def wrap(s, fs, width):
    out = []
    for para in s.split("\n"):
        line = ""
        words = []
        for w in para.split(" "):
            if len(w) * fs * CHAR > width and "/" in w:  # a path: break it before a slash
                parts, cur = [], ""
                for chunk in re.split(r"(?=/)", w):
                    if cur and len(cur + chunk) * fs * CHAR > width:
                        parts.append(cur)
                        cur = ""
                    cur += chunk
                words += parts + [cur]
            else:
                words.append(w)
        for w in words:
            t = (line + " " + w).strip()
            if len(t) * fs * CHAR <= width or not line:
                line = t
            else:
                out.append(line)
                line = w
        out.append(line)
    return out


def text_height(s, fs, wrap_w=None):
    return len(wrap(s, fs, wrap_w or 10**6)) * fs * LH


class Canvas:
    def __init__(self):
        self.rnd = random.Random(7)
        self.els, self.svg, self.n = [], [], 0

    def base(self, t, x, y, w, h, **kw):
        self.n += 1
        e = dict(id=kw.pop("id", None) or f"{t[:3]}{self.n:04d}", type=t, x=x, y=y, width=w, height=h, angle=0,
                 strokeColor=INK, backgroundColor="transparent", fillStyle="solid", strokeWidth=1,
                 strokeStyle="solid", roughness=0, opacity=100, groupIds=[], frameId=None, roundness=None,
                 seed=self.rnd.randint(1, 2**31 - 1), version=1, versionNonce=self.rnd.randint(1, 2**31 - 1),
                 isDeleted=False, boundElements=[], updated=1760000000000, link=None, locked=False)
        e.update(kw)
        return e

    def rect(self, x, y, w, h, fill, stroke, sw=1, dashed=False, groups=None, id=None, radius=10):
        e = self.base("rectangle", x, y, w, h, backgroundColor=fill, strokeColor=stroke, strokeWidth=sw,
                      strokeStyle="dashed" if dashed else "solid", groupIds=groups or [], id=id,
                      roundness={"type": 3} if radius else None)
        self.els.append(e)
        self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{fill}" stroke="{stroke}" '
                        f'stroke-width="{sw}"' + (' stroke-dasharray="7 5"' if dashed else '') + '/>')
        return e

    def text(self, x, y, w, s, fs=15, color=INK, align="left", groups=None, wrap_w=None, bold=False):
        lines = wrap(s, fs, wrap_w or 10**6)
        body = "\n".join(lines)
        h = len(lines) * fs * LH
        self.els.append(self.base("text", x, y, w, h, strokeColor=color, groupIds=groups or [], text=body, fontSize=fs,
                                  fontFamily=FONT, textAlign=align, verticalAlign="top", baseline=int(fs * 0.9),
                                  containerId=None, originalText=body, autoResize=False, lineHeight=LH))
        anchor = {"left": "start", "center": "middle", "right": "end"}[align]
        tx = x if align == "left" else (x + w / 2 if align == "center" else x + w)
        for i, ln in enumerate(lines):
            self.svg.append(f'<text x="{tx}" y="{y + i * fs * LH + fs * 0.92}" font-size="{fs}" fill="{color}" '
                            f'text-anchor="{anchor}" font-family="Nunito, Helvetica, Arial, sans-serif"'
                            + (' font-weight="700"' if bold else '') + f'>{escape(ln)}</text>')
        return h

    def ellipse(self, x, y, w, h, fill, stroke, sw=2, dashed=False):
        e = self.base("ellipse", x, y, w, h, backgroundColor=fill, strokeColor=stroke, strokeWidth=sw,
                      strokeStyle="dashed" if dashed else "solid", roundness={"type": 2})
        self.els.append(e)
        self.svg.append(f'<ellipse cx="{x + w / 2}" cy="{y + h / 2}" rx="{w / 2}" ry="{h / 2}" fill="{fill}" stroke="{stroke}" '
                        f'stroke-width="{sw}"' + (' stroke-dasharray="7 5"' if dashed else '') + '/>')
        return e

    def diamond(self, x, y, w, h, fill, stroke, sw=2):
        e = self.base("diamond", x, y, w, h, backgroundColor=fill, strokeColor=stroke, strokeWidth=sw, roundness={"type": 2})
        self.els.append(e)
        pts = f"{x + w / 2},{y} {x + w},{y + h / 2} {x + w / 2},{y + h} {x},{y + h / 2}"
        self.svg.append(f'<polygon points="{pts}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"/>')
        return e

    def vtext(self, cx, cy, length, s, fs=13, color=INK, bold=False):
        """Text read bottom to top, centred on (cx, cy), at most `length` long: a lane's label."""
        lines = wrap(s, fs, length)
        body = "\n".join(lines)
        h = len(lines) * fs * LH
        e = self.base("text", cx - length / 2, cy - h / 2, length, h, strokeColor=color, text=body, fontSize=fs,
                      fontFamily=FONT, textAlign="center", verticalAlign="top", baseline=int(fs * 0.9),
                      containerId=None, originalText=body, autoResize=False, lineHeight=LH, angle=-1.5707963267948966)
        self.els.append(e)
        for i, ln in enumerate(lines):
            ty = cy - h / 2 + i * fs * LH + fs * 0.92
            self.svg.append(f'<text x="{cx}" y="{ty}" font-size="{fs}" fill="{color}" text-anchor="middle" transform="rotate(-90 {cx} {cy})" '
                            f'font-family="Nunito, Helvetica, Arial, sans-serif"' + (' font-weight="700"' if bold else '') + f'>{escape(ln)}</text>')

    def arrow(self, pts, color, dashed, both, start, end, plain=False):
        x0, y0 = pts[0]
        rel = [[px - x0, py - y0] for px, py in pts]
        w = max(p[0] for p in rel) - min(p[0] for p in rel)
        h = max(p[1] for p in rel) - min(p[1] for p in rel)
        e = self.base("arrow", x0, y0, w, h, strokeColor=color, strokeWidth=2, strokeStyle="dashed" if dashed else "solid",
                      points=rel, lastCommittedPoint=None,
                      startBinding={"elementId": start, "focus": 0, "gap": 4} if start else None,
                      endBinding={"elementId": end, "focus": 0, "gap": 4} if end else None,
                      startArrowhead="arrow" if both else None, endArrowhead=None if plain else "arrow", elbowed=False)
        self.els.append(e)
        self.svg.append(f'<path d="M {" L ".join(f"{px} {py}" for px, py in pts)}" fill="none" stroke="{color}" '
                        f'stroke-width="2" stroke-linejoin="round"' + ('' if plain else ' marker-end="url(#ah)"')
                        + (' marker-start="url(#ah)"' if both else '') + (' stroke-dasharray="7 5"' if dashed else '') + '/>')
        return e

    def save(self, width, height, out_excalidraw, out_svg=None):
        with open(out_excalidraw, "w") as f:
            json.dump(dict(type="excalidraw", version=2, source="https://excalidraw.com", elements=self.els,
                           appState=dict(gridSize=None, viewBackgroundColor="#ffffff"), files={}), f, indent=2)
        if out_svg:
            head = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">'
                    '<defs><marker id="ah" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
                    '<path d="M 0 1 L 9 5 L 0 9" fill="none" stroke="context-stroke" stroke-width="1.6"/></marker></defs>'
                    f'<rect width="{width}" height="{height}" fill="#ffffff"/>')
            with open(out_svg, "w") as f:
                f.write(head + "".join(self.svg) + "</svg>")
