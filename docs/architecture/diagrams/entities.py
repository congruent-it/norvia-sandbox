#!/usr/bin/env python3
"""The entity relationship diagrams of the application pages: one per application with a database.

    python3 entities.py [slug ...]    # writes er-<slug>.excalidraw and .png; no slugs draws them all

Reads er-<slug>.json, which Norvia.Docs.Generator writes from the EF Core model (dotnet run --project src/Docs/Norvia.Docs.Generator),
and draws it in the graph style (see README.md). Tables that reference nothing and are referenced by nothing sit in a row below.
The PNGs need rsvg-convert.
"""
import json
import sys
from pathlib import Path

from excalidraw_graph import render_all
from excalidraw_matrix import PALETTE

OUT = Path(__file__).parent
L = lambda label, key: (label, PALETTE[key][2], PALETTE[key][3], False)

APPLICATIONS = {
    "tms": ("TMS", "blue"),
    "erp": ("ERP", "green"),
    "partner-portal": ("Partner portal", "gray"),
    "driver-app": ("Driver app", "teal"),
    "simulator": ("Simulator", "orange"),
}
LOOSE_PER_ROW = 4


def cardinality(relationship):
    one = "0..1" if relationship["optional"] else "1"
    many = "0..1" if relationship["oneToOne"] else "n"
    return f"{one} to {many}"


def layout(model):
    """Columns by depth from the referenced tables, rows near the tables that reference them. Returns {table: (col, row)}."""
    names = [entity["name"] for entity in model["entities"]]
    links = [(r["principal"], r["dependent"]) for r in model["relationships"] if r["principal"] != r["dependent"]]
    linked = {name for link in links for name in link}
    depth = {name: 0 for name in linked}
    for _ in range(len(linked)):
        for principal, dependent in links:
            depth[dependent] = max(depth[dependent], depth[principal] + 1)

    # a tree: each table sits under the deepest table that references it, level with the middle of its dependents
    parent = {}
    for name in sorted(linked, key=names.index):
        parent[name] = next((p for p, d in links if d == name and depth[p] == depth[name] - 1), None)
    position, free = {}, [0.0]

    def place(name):
        children = [c for c in sorted(linked, key=names.index) if parent[c] == name]
        for child in children:
            place(child)
        row = (position[children[0]][1] + position[children[-1]][1]) / 2 if children else free[0]
        position[name] = (depth[name], row)
        if not children:
            free[0] += 1

    for root in (n for n in sorted(linked, key=names.index) if parent[n] is None):
        place(root)
        free[0] = max(free[0], max(row for _, row in position.values()) + 1) + 0.4

    below = max((row for _, row in position.values()), default=-1) + 1.6
    for index, name in enumerate(n for n in names if n not in linked):
        position[name] = (index % LOOSE_PER_ROW, below + index // LOOSE_PER_ROW)
    return position


def spec(slug):
    model = json.loads((OUT / f"er-{slug}.json").read_text())
    application, palette = APPLICATIONS[slug]
    position = layout(model)
    related = {r["principal"] for r in model["relationships"]} | {r["dependent"] for r in model["relationships"]}
    return dict(
        title=f"{application} · entity relationships",
        subtitle="The tables of the database and the foreign keys between them. Only the key columns are drawn; every column is in the application page.",
        footnote=f"Source of truth: er-{slug}.json, which Norvia.Docs.Generator writes from the EF Core model. An arrow runs from the referenced table to the table that holds the foreign key, "
                 "labelled with its foreign key column and its cardinality: 1 to n, with 0..1 in place of 1 for a nullable key and in place of n for a unique one. "
                 "Tables with no foreign key to or from them sit in the rows below.",
        legend=[L("A table", palette)],
        cell=(460, 130),
        nodes=[dict(
            id=entity["name"], col=position[entity["name"]][0], row=position[entity["name"]][1], palette=palette,
            title=entity["name"], w=300,
            sub="\n".join(f'{" ".join(column["keys"])}  {column["name"]}  ({column["type"]})' for column in entity["columns"])
            or ("Referenced by nothing" if entity["name"] not in related else ""),
        ) for entity in model["entities"]],
        groups=[],
        edges=[(r["principal"], r["dependent"], f'{", ".join(r["columns"])} · {cardinality(r)}') for r in model["relationships"]],
    )


if __name__ == "__main__":
    slugs = sys.argv[1:] or list(APPLICATIONS)
    render_all({f"er-{slug}": spec(slug) for slug in slugs}, OUT, "")
