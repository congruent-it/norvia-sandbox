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
}
