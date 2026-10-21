using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Docs.Generator;

/// <summary>
/// An application's entities and relationships as JSON, which docs/architecture/diagrams/entities.py draws as an Excalidraw diagram.
/// Only key columns are listed, as the picture only shows those.
/// </summary>
internal static class EntityDiagram
{
    private const string Folder = "docs/architecture/diagrams";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The model's path from the repository root, or, by default, as a page in docs/applications links to it.</summary>
    public static string JsonPath(string slug, bool fromRoot = false) => $"{(fromRoot ? Folder : "../architecture/diagrams")}/er-{slug}.json";

    public static string ExcalidrawPath(string slug) => $"../architecture/diagrams/er-{slug}.excalidraw";

    public static string PngPath(string slug) => $"../architecture/diagrams/er-{slug}.png";

    public static string Json(IModel model)
    {
        var tables = model.GetRelationalModel().Tables
            .OrderBy(table => table.Schema, StringComparer.Ordinal).ThenBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();

        var entities = tables.Select(table => new
        {
            name = table.Name,
            columns = table.Columns
                .Where(column => DatabaseSection.IsPrimaryKey(table, column) || DatabaseSection.IsForeignKey(table, column))
                .Select(column => new
                {
                    name = column.Name,
                    type = column.StoreType,
                    keys = new[] { DatabaseSection.IsPrimaryKey(table, column) ? "PK" : null, DatabaseSection.IsForeignKey(table, column) ? "FK" : null }.OfType<string>().ToArray(),
                }),
        });

        var relationships = tables.SelectMany(table => table.ForeignKeyConstraints
            .OrderBy(foreignKey => foreignKey.Name, StringComparer.Ordinal)
            .Select(foreignKey => new
            {
                principal = foreignKey.PrincipalTable.Name,
                dependent = table.Name,
                columns = foreignKey.Columns.Select(column => column.Name).ToArray(),
                optional = foreignKey.Columns.Any(column => column.IsNullable),
                oneToOne = DatabaseSection.IsUnique(table, foreignKey.Columns),
            }));

        return JsonSerializer.Serialize(new { entities, relationships }, Options) + "\n";
    }

    /// <summary>The tables the Excalidraw file draws: a node's title is the text element before its subtitle, which starts with a key column.</summary>
    public static IReadOnlySet<string> DrawnTables(string excalidraw)
    {
        using var diagram = JsonDocument.Parse(excalidraw);
        var texts = diagram.RootElement.GetProperty("elements").EnumerateArray()
            .Where(element => element.GetProperty("type").GetString() == "text")
            .Select(element => element.GetProperty("text").GetString() ?? string.Empty)
            .ToList();

        return texts.Skip(1).Select((text, index) => (Text: text, Title: texts[index]))
            .Where(pair => pair.Text.StartsWith("PK ", StringComparison.Ordinal) || pair.Text.StartsWith("FK ", StringComparison.Ordinal))
            .Select(pair => pair.Title)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The tables with a key column in a model's JSON, which the Excalidraw file has to draw.</summary>
    public static IReadOnlySet<string> ModelledTables(string json)
    {
        using var model = JsonDocument.Parse(json);
        return model.RootElement.GetProperty("entities").EnumerateArray()
            .Where(entity => entity.GetProperty("columns").GetArrayLength() > 0)
            .Select(entity => entity.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static bool IsModelPath(string path) => path.StartsWith($"{Folder}/er-", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal);

    public static string ExcalidrawPathOf(string modelPath) => string.Concat(modelPath.AsSpan(0, modelPath.Length - ".json".Length), ".excalidraw");
}
