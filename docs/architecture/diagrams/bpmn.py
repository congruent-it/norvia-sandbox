#!/usr/bin/env python3
"""Draws the BPMN models of docs/processes in Norvia's Excalidraw style.

    python3 bpmn.py [process ...]    # e.g. p1-transport-order-intake; writes bpmn-<process>.excalidraw and .png here

The .bpmn files stay the source: they open in any BPMN tool and carry their own layout (the bpmndi section), which is kept. This only
restyles what they say: a person's task is yellow, a system's takes its lane's colour, events are rings, gateways diamonds. Needs rsvg-convert for the PNGs.
"""
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from excalidraw_canvas import ARROW, ARROW_PLANNED, INK, MUTED, SOFT, Canvas, text_height
from excalidraw_matrix import PALETTE, PERSON

HERE = Path(__file__).parent
SOURCES = HERE.parent.parent / "processes" / "tms"
NS = {"b": "http://www.omg.org/spec/BPMN/20100524/MODEL", "d": "http://www.omg.org/spec/BPMN/20100524/DI",
      "c": "http://www.omg.org/spec/DD/20100524/DC", "i": "http://www.omg.org/spec/DD/20100524/DI"}
M, TOP = 40, 120
STRIP = 30  # width of a lane's or pool's label strip


def palette_of(name):
    n = (name or "").lower()
    for words, key in [(("driver app", "driver"), "teal"), (("erp", "finance"), "green"), (("api management", "logic app", "service bus", "queue", "topic"), "violet"),
                       (("tms", "planner", "customer service"), "blue"), (("portal", "shipper", "carrier", "persona", "partner"), "gray")]:
        if any(w in n for w in words):
            return key
    return "blue"


def local(tag):
    return tag.split("}")[1]


def render(path, out_excalidraw, out_svg):
    root = ET.parse(path).getroot()
    elements, lane_of, lanes, pools = {}, {}, {}, {}
    for el in root.iter():
        if el.get("id") and el.tag.startswith("{" + NS["b"] + "}"):
            elements[el.get("id")] = el
    for lane in root.iterfind(".//b:lane", NS):
        lanes[lane.get("id")] = lane.get("name", "")
        for ref in lane.iterfind("b:flowNodeRef", NS):
            lane_of[ref.text] = lane.get("name", "")
    for p in root.iterfind(".//b:participant", NS):
        pools[p.get("id")] = p.get("name", "")
    title = next(iter(pools.values()), path.stem)

    shapes, labels, edges = {}, {}, {}
    for sh in root.iterfind(".//d:BPMNShape", NS):
        b = sh.find("c:Bounds", NS)
        shapes[sh.get("bpmnElement")] = (float(b.get("x")), float(b.get("y")), float(b.get("width")), float(b.get("height")), sh.get("isMarkerVisible") == "true")
        lb = sh.find("d:BPMNLabel/c:Bounds", NS)
        if lb is not None:
            labels[sh.get("bpmnElement")] = tuple(float(lb.get(k)) for k in ("x", "y", "width", "height"))
    for ed in root.iterfind(".//d:BPMNEdge", NS):
        pts = [(float(w.get("x")), float(w.get("y"))) for w in ed.iterfind("i:waypoint", NS)]
        lb = ed.find("d:BPMNLabel/c:Bounds", NS)
        edges[ed.get("bpmnElement")] = (pts, tuple(float(lb.get(k)) for k in ("x", "y", "width", "height")) if lb is not None else None)

    xs = [s[0] for s in shapes.values()] + [p[0] for pts, _ in edges.values() for p in pts]
    ys = [s[1] for s in shapes.values()] + [p[1] for pts, _ in edges.values() for p in pts]
    xe = [s[0] + s[2] for s in shapes.values()] + [p[0] for pts, _ in edges.values() for p in pts]
    ye = [s[1] + s[3] for s in shapes.values()] + [p[1] for pts, _ in edges.values() for p in pts]
    dx, dy = M - min(xs), TOP - min(ys)
    width, height = max(xe) - min(xs) + 2 * M, max(ye) - min(ys) + TOP + 70
    T = lambda x, y: (x + dx, y + dy)

    cv = Canvas()
    text, rect = cv.text, cv.rect
    text(M, 30, width - 2 * M, f"{title} · BPMN", 34, INK, bold=True)
    text(M, 74, width - 2 * M, "The process as modelled in the .bpmn file beside its page, in Norvia's colours.", 15, MUTED)
    legend = [("A person acts", *PERSON, False), ("A system acts", PALETTE["blue"][2], PALETTE["blue"][3], False)]
    lx = M
    for lab, fill, stroke, dashed in legend:
        rect(lx, 98, 26, 18, fill, stroke, 2, dashed)
        text(lx + 34, 99, len(lab) * 8.6 + 6, lab, 14, INK)
        lx += 34 + len(lab) * 8.6 + 40

    # pools and lanes, the biggest first
    frames = sorted([(i, n, "pool") for i, n in pools.items() if i in shapes] + [(i, n, "lane") for i, n in lanes.items() if i in shapes],
                    key=lambda t: -(shapes[t[0]][2] * shapes[t[0]][3]))
    for i, name, kind in frames:
        x, y, w, h, _ = shapes[i]
        x, y = T(x, y)
        pal = PALETTE[palette_of(name)]
        rect(x, y, w, h, "#ffffff" if kind == "pool" else pal[1], pal[3] if kind == "lane" else "#adb5bd", 1.5, radius=4)
        if kind == "lane" or elements[i].get("processRef"):
            rect(x, y, STRIP, h, pal[0], pal[0], 1, radius=4)
            cv.vtext(x + STRIP / 2, y + h / 2, h - 12, name, 12, "#ffffff", bold=True)
        else:
            rect(x, y, w, h, "#ffffff", "#adb5bd", 1.5, dashed=True, radius=4)
            text(x + 12, y + h / 2 - 8, w - 24, name, 13, MUTED, "center", wrap_w=w - 24, bold=True)

    # sequence and message flows
    for fid, (pts, lb) in edges.items():
        el = elements.get(fid)
        if el is None:
            continue
        message = local(el.tag) == "messageFlow"
        cv.arrow([T(*p) for p in pts], ARROW_PLANNED if message else ARROW, message, False, None, None)
        label = el.get("name")
        if label and lb:
            lx, ly, lw, lh = lb
            x, y = T(lx, ly)
            rect(x - 2, y - 1, lw + 4, lh + 2, "#ffffff", "#ffffff", 0, radius=3)
            text(x, y, lw, label, 11, INK, "center", wrap_w=lw + 8)

    # tasks, gateways and events
    for i, el in ((i, elements[i]) for i in shapes if i in elements):
        kind = local(el.tag)
        x, y, w, h, marker = shapes[i]
        x, y = T(x, y)
        name = el.get("name", "")
        lane_pal = PALETTE[palette_of(lane_of.get(i, ""))]
        if kind in ("serviceTask", "userTask", "businessRuleTask", "callActivity", "task", "sendTask", "receiveTask", "scriptTask", "manualTask"):
            person = kind in ("userTask", "manualTask")
            fill, stroke = (PERSON if person else (lane_pal[2], lane_pal[3]))
            rect(x, y, w, h, fill, stroke, 3 if kind == "callActivity" else 2)
            fs = 12
            while fs > 9 and text_height(name, fs, w - 14) > h - 8:  # a long label shrinks to fit its task
                fs -= 1
            th = text_height(name, fs, w - 14)
            text(x + 6, y + (h - th) / 2, w - 12, name, fs, INK, "center", wrap_w=w - 14)
        elif kind.endswith("Gateway"):
            cv.diamond(x, y, w, h, "#ffffff", "#495057", 2)
            cx, cy, r = x + w / 2, y + h / 2, min(w, h) * 0.2
            if kind == "parallelGateway":
                cv.arrow([(cx - r, cy), (cx + r, cy)], "#495057", False, False, None, None, plain=True)
                cv.arrow([(cx, cy - r), (cx, cy + r)], "#495057", False, False, None, None, plain=True)
            elif marker or kind == "exclusiveGateway":
                cv.arrow([(cx - r, cy - r), (cx + r, cy + r)], "#495057", False, False, None, None, plain=True)
                cv.arrow([(cx - r, cy + r), (cx + r, cy - r)], "#495057", False, False, None, None, plain=True)
        elif kind.endswith("Event"):
            if kind == "startEvent":
                ring, sw = "#2f9e44", 2
            elif kind == "endEvent":
                ring, sw = "#495057", 4
            else:
                ring, sw = "#1c7ed6", 2
            cv.ellipse(x, y, w, h, "#ffffff", ring, sw, dashed=el.get("cancelActivity") == "false")
            if kind in ("intermediateThrowEvent", "intermediateCatchEvent", "boundaryEvent"):
                cv.ellipse(x + 4, y + 4, w - 8, h - 8, "#ffffff", ring, 1)
            cx, cy = x + w / 2, y + h / 2
            for d in el:
                dk = local(d.tag)
                if dk == "messageEventDefinition":
                    solid = kind in ("endEvent", "intermediateThrowEvent")
                    rect(cx - 7, cy - 5, 14, 10, "#495057" if solid else "#ffffff", "#495057", 1.5, radius=0)
                    cv.arrow([(cx - 7, cy - 5), (cx, cy + 1), (cx + 7, cy - 5)], "#ffffff" if solid else "#495057", False, False, None, None, plain=True)
                elif dk == "errorEventDefinition":
                    text(cx - 6, cy - 9, 12, "!", 16, "#c92a2a", "center", bold=True)
                elif dk == "timerEventDefinition":
                    cv.ellipse(cx - 7, cy - 7, 14, 14, "#ffffff", "#495057", 1.5)
                    cv.arrow([(cx, cy - 4), (cx, cy), (cx + 3, cy + 2)], "#495057", False, False, None, None, plain=True)
                elif dk == "conditionalEventDefinition":
                    text(cx - 6, cy - 9, 12, "?", 16, "#495057", "center", bold=True)
        elif kind == "textAnnotation":
            rect(x, y, w, h, "#fff9db", "#f59f00", 1, radius=4)
            body = (el.findtext("b:text", default="", namespaces=NS) or "").strip()
            text(x + 6, y + 5, w - 12, body, 11, INK, wrap_w=w - 14)
        lb = labels.get(i)
        if name and not lb and kind == "boundaryEvent" and el.get("attachedToRef") in shapes:
            # no position in the file: on the outside of the task edge the event sits on, never over the task's own text
            ax, ay, aw, ah, _ = shapes[el.get("attachedToRef")]
            ex, ey = x - dx, y - dy
            cx, cy = ex + w / 2, ey + h / 2
            side = min((abs(cy - ay), "top"), (abs(cy - (ay + ah)), "bottom"), (abs(cx - ax), "left"), (abs(cx - (ax + aw)), "right"))[1]
            lb = {"top": (cx - 45, ey - 30, 90, 28), "bottom": (cx - 45, ey + h + 3, 90, 28),
                  "left": (ex - 96, cy - 14, 90, 28), "right": (ex + w + 6, cy - 14, 90, 28)}[side]
        elif name and not lb and (kind.endswith("Event") or kind.endswith("Gateway")):  # no position in the file: under the shape
            lb = (x - dx - 30, y - dy + h + 3, w + 60, 30)
        if lb and name and (kind.endswith("Event") or kind.endswith("Gateway")):
            lx, ly, lw, lh = lb
            lxx, lyy = T(lx, ly)
            text(lxx, lyy, lw, name, 11, INK, "center", wrap_w=lw + 8)

    cv.save(width, height, out_excalidraw, out_svg)


def main(names):
    for path in sorted(SOURCES.glob("*.bpmn")):
        if names and path.stem not in names:
            continue
        out = HERE / f"bpmn-{path.stem}.excalidraw"
        svg = f"/tmp/bpmn-{path.stem}.svg"
        render(path, out, svg)
        try:
            subprocess.run(["rsvg-convert", "-z", "1", svg, "-o", str(out.with_suffix(".png"))], check=True)
        except FileNotFoundError:
            print("rsvg-convert not found: wrote the .excalidraw only")
        print("wrote", out.name)


if __name__ == "__main__":
    main(sys.argv[1:])
