using System.Text.Json;

namespace Norvia.Docs.Generator;

/// <summary>
/// The hand-written descriptions of one application's tables and columns, read from <c>docs/applications/descriptions/&lt;app&gt;.json</c>:
/// <c>{ "tables": { "Shipments": { "description": "...", "columns": { "Id": "..." } } } }</c>.
/// </summary>
internal sealed class ApplicationDescriptions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ApplicationDescriptions None { get; } = new();

    public Dictionary<string, TableDescription> Tables { get; init; } = new(StringComparer.Ordinal);

    public static ApplicationDescriptions Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? None : JsonSerializer.Deserialize<ApplicationDescriptions>(json, Options) ?? None;

    public string? OfTable(string table) => Tables.GetValueOrDefault(table)?.Description;

    public string? OfColumn(string table, string column) => Tables.GetValueOrDefault(table)?.Columns.GetValueOrDefault(column);
}

internal sealed class TableDescription
{
    public string? Description { get; init; }

    public Dictionary<string, string> Columns { get; init; } = new(StringComparer.Ordinal);
}
