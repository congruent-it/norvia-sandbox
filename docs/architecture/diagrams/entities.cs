#:project ../../../src/Docs/Norvia.Docs.Diagrams/Norvia.Docs.Diagrams.csproj

// The entity relationship diagrams of the application pages: one per application with a database.
//
//     dotnet run docs/architecture/diagrams/entities.cs -- [slug ...]    # writes er-<slug>.excalidraw and .png; no slugs draws them all
//
// Reads er-<slug>.json, which Norvia.Docs.Generator writes from the EF Core model (dotnet run --project src/Docs/Norvia.Docs.Generator),
// and draws it in the graph style (see README.md). Tables that reference nothing and are referenced by nothing sit in a row below.
// The PNGs need rsvg-convert.

using System.Text.Json.Nodes;
using Norvia.Docs.Diagrams;

const int LoosePerRow = 4;

Dictionary<string, (string Name, string Palette)> applications = new()
{
    ["tms"] = ("TMS", "blue"),
    ["erp"] = ("ERP", "green"),
    ["partner-portal"] = ("Partner portal", "gray"),
    ["driver-app"] = ("Driver app", "teal"),
    ["simulator"] = ("Simulator", "orange"),
};

var directory = DiagramOutput.SpecDirectory();
var slugs = args.Length > 0 ? args : [.. applications.Keys];
var unknown = slugs.Where(slug => !applications.ContainsKey(slug)).ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine($"Unknown application {string.Join(", ", unknown)}; known: {string.Join(", ", applications.Keys)}");
    return 1;
}

var specs = slugs.ToDictionary(slug => $"er-{slug}", slug => SpecOf(slug));
DiagramOutput.RenderAll(specs, directory, "", [], GraphDiagram.Render);
return 0;

static string Cardinality(JsonNode relationship)
{
    var one = (bool)relationship["optional"]! ? "0..1" : "1";
    var many = (bool)relationship["oneToOne"]! ? "0..1" : "n";
    return $"{one} to {many}";
}

// Columns by depth from the referenced tables, rows near the tables that reference them. Returns {table: (col, row)}.
static Dictionary<string, (int Col, double Row)> Layout(JsonNode model)
{
    var names = model["entities"]!.AsArray().Select(e => (string)e!["name"]!).ToList();
    var links = model["relationships"]!.AsArray()
        .Select(r => (Principal: (string)r!["principal"]!, Dependent: (string)r["dependent"]!))
        .Where(l => l.Principal != l.Dependent)
        .ToList();
    var linked = links.SelectMany(l => new[] { l.Principal, l.Dependent }).ToHashSet();
    var inOrder = linked.OrderBy(names.IndexOf).ToList();
    var depth = linked.ToDictionary(name => name, _ => 0);
    for (var pass = 0; pass < linked.Count; pass++)
    {
        foreach (var (principal, dependent) in links)
        {
            depth[dependent] = Math.Max(depth[dependent], depth[principal] + 1);
        }
    }

    // a tree: each table sits under the deepest table that references it, level with the middle of its dependents
    var parent = new Dictionary<string, string?>();
    foreach (var name in inOrder)
    {
        parent[name] = links.Where(l => l.Dependent == name && depth[l.Principal] == depth[name] - 1).Select(l => l.Principal).FirstOrDefault();
    }

    Dictionary<string, (int Col, double Row)> position = [];
    var free = 0.0;

    void Place(string name)
    {
        var children = inOrder.Where(c => parent[c] == name).ToList();
        foreach (var child in children)
        {
            Place(child);
        }

        var row = children.Count > 0 ? (position[children[0]].Row + position[children[^1]].Row) / 2 : free;
        position[name] = (depth[name], row);
        if (children.Count == 0)
        {
            free += 1;
        }
    }

    foreach (var root in inOrder.Where(n => parent[n] is null))
    {
        Place(root);
        free = Math.Max(free, position.Values.Max(p => p.Row) + 1) + 0.4;
    }

    var below = (position.Count > 0 ? position.Values.Max(p => p.Row) : -1) + 1.6;
    var index = 0;
    foreach (var name in names.Where(n => !linked.Contains(n)))
    {
        position[name] = (index % LoosePerRow, below + (index / LoosePerRow));
        index++;
    }

    return position;
}

GraphSpec SpecOf(string slug)
{
    var model = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, $"er-{slug}.json")))!;
    var (application, palette) = applications[slug];
    var position = Layout(model);
    var relationships = model["relationships"]!.AsArray();
    var related = relationships.SelectMany(r => new[] { (string)r!["principal"]!, (string)r["dependent"]! }).ToHashSet();
    return new GraphSpec
    {
        Title = $"{application} · entity relationships",
        Subtitle = "The tables of the database and the foreign keys between them. Only the key columns are drawn; every column is in the application page.",
        Footnote = $"Source of truth: er-{slug}.json, which Norvia.Docs.Generator writes from the EF Core model. An arrow runs from the referenced table to the table that holds the foreign key, "
            + "labelled with its foreign key column and its cardinality: 1 to n, with 0..1 in place of 1 for a nullable key and in place of n for a unique one. "
            + "Tables with no foreign key to or from them sit in the rows below.",
        Legend = [Palette.Legend("A table", palette)],
        Cell = (460, 130),
        Nodes =
        [
            .. model["entities"]!.AsArray().Select(entity =>
            {
                var name = (string)entity!["name"]!;
                var columns = string.Join("\n", entity["columns"]!.AsArray().Select(column =>
                    $"{string.Join(' ', column!["keys"]!.AsArray().Select(k => (string)k!))}  {(string)column["name"]!}  ({(string)column["type"]!})"));
                return new GraphNode
                {
                    Id = name,
                    Col = position[name].Col,
                    Row = position[name].Row,
                    Palette = palette,
                    Title = name,
                    W = 300,
                    Sub = columns.Length > 0 ? columns : (related.Contains(name) ? "" : "Referenced by nothing"),
                };
            }),
        ],
        Groups = [],
        Edges =
        [
            .. relationships.Select(r => new GraphEdge(
                (string)r!["principal"]!,
                (string)r["dependent"]!,
                $"{string.Join(", ", r["columns"]!.AsArray().Select(c => (string)c!))} · {Cardinality(r)}")),
        ],
    };
}
