using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norvia.Docs.Generator;

/// <summary>Writes an application's database as Markdown: a link to its ER diagram, then each table with its columns, keys and indexes, then its sequences.</summary>
internal static class DatabaseSection
{
    public static string Render(IModel model, ApplicationDescriptions descriptions, string slug)
    {
        var relational = model.GetRelationalModel();
        var tables = relational.Tables.OrderBy(table => table.Schema, StringComparer.Ordinal).ThenBy(table => table.Name, StringComparer.Ordinal).ToArray();

        var text = new StringBuilder();
        text.AppendLine("### Entity relationships").AppendLine();
        text.AppendLine("Only the key columns are drawn; every column is in the tables below. The picture is drawn from [the model](" + EntityDiagram.JsonPath(slug) + ") by `docs/architecture/diagrams/entities.py`, and its [Excalidraw file](" + EntityDiagram.ExcalidrawPath(slug) + ") opens in Excalidraw.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"![{slug} entity relationships]({EntityDiagram.PngPath(slug)})").AppendLine();

        text.AppendLine("### Tables").AppendLine();
        foreach (var table in tables)
        {
            AppendTable(text, table, descriptions);
        }

        var sequences = relational.Sequences.OrderBy(sequence => sequence.Name, StringComparer.Ordinal).ToArray();
        if (sequences.Length > 0)
        {
            text.AppendLine("### Sequences").AppendLine();
            text.AppendLine("| Sequence | Type | Starts at | Increments by |").AppendLine("|---|---|---|---|");
            foreach (var sequence in sequences)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| `{sequence.Name}` | `{SqlTypeOf(sequence.Type)}` | {sequence.StartValue} | {sequence.IncrementBy} |");
            }
        }

        return text.ToString();
    }

    private static void AppendTable(StringBuilder text, ITable table, ApplicationDescriptions descriptions)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"#### `{table.Name}`").AppendLine();
        if (descriptions.OfTable(table.Name) is { Length: > 0 } description)
        {
            text.AppendLine(description).AppendLine();
        }

        text.AppendLine("| Column | Type | Nullable | Default | Key | Description |").AppendLine("|---|---|---|---|---|---|");
        foreach (var column in table.Columns)
        {
            var keys = string.Join(", ", new[] { IsPrimaryKey(table, column) ? "PK" : null, IsForeignKey(table, column) ? "FK" : null }.OfType<string>());
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{column.Name}` | `{Cell(column.StoreType)}` | {(column.IsNullable ? "yes" : "no")} | {Cell(DefaultOf(table, column))} | {keys} | {Cell(descriptions.OfColumn(table.Name, column.Name))} |");
        }

        text.AppendLine();

        if (table.PrimaryKey is { } primaryKey)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Primary key `{primaryKey.Name}`: {ColumnList(primaryKey.Columns)}.").AppendLine();
        }

        var foreignKeys = table.ForeignKeyConstraints.OrderBy(foreignKey => foreignKey.Name, StringComparer.Ordinal).ToArray();
        if (foreignKeys.Length > 0)
        {
            text.AppendLine("| Foreign key | Columns | References | On delete |").AppendLine("|---|---|---|---|");
            foreach (var foreignKey in foreignKeys)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{foreignKey.Name}` | {ColumnList(foreignKey.Columns)} | `{foreignKey.PrincipalTable.Name}` ({ColumnList(foreignKey.PrincipalColumns)}) | {OnDelete(foreignKey.OnDeleteAction)} |");
            }

            text.AppendLine();
        }

        var uniques = table.UniqueConstraints.Where(constraint => constraint != table.PrimaryKey).OrderBy(constraint => constraint.Name, StringComparer.Ordinal).ToArray();
        if (uniques.Length > 0)
        {
            text.AppendLine("| Unique constraint | Columns |").AppendLine("|---|---|");
            foreach (var constraint in uniques)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| `{constraint.Name}` | {ColumnList(constraint.Columns)} |");
            }

            text.AppendLine();
        }

        var indexes = table.Indexes.OrderBy(index => index.Name, StringComparer.Ordinal).ToArray();
        if (indexes.Length > 0)
        {
            text.AppendLine("| Index | Columns | Unique | Filter |").AppendLine("|---|---|---|---|");
            foreach (var index in indexes)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{index.Name}` | {ColumnList(index.Columns)} | {(index.IsUnique ? "yes" : "no")} | {(index.Filter is null ? string.Empty : $"`{Cell(index.Filter)}`")} |");
            }

            text.AppendLine();
        }

        var checks = table.CheckConstraints.OrderBy(check => check.Name, StringComparer.Ordinal).ToArray();
        if (checks.Length > 0)
        {
            text.AppendLine("| Check constraint | Expression |").AppendLine("|---|---|");
            foreach (var check in checks)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| `{check.Name}` | `{Cell(check.Sql)}` |");
            }

            text.AppendLine();
        }
    }

    internal static bool IsPrimaryKey(ITable table, IColumn column) => table.PrimaryKey?.Columns.Contains(column) == true;

    internal static bool IsForeignKey(ITable table, IColumn column) => table.ForeignKeyConstraints.Any(foreignKey => foreignKey.Columns.Contains(column));

    internal static bool IsUnique(ITable table, IReadOnlyList<IColumn> columns) =>
        SameColumns(table.PrimaryKey?.Columns, columns)
        || table.UniqueConstraints.Any(constraint => SameColumns(constraint.Columns, columns))
        || table.Indexes.Any(index => index.IsUnique && index.Filter is null && SameColumns(index.Columns, columns));

    private static bool SameColumns(IReadOnlyList<IColumn>? left, IReadOnlyList<IColumn> right) =>
        left is not null && left.Count == right.Count && left.All(right.Contains);

    private static string DefaultOf(ITable table, IColumn column)
    {
        if (column.ComputedColumnSql is { } computed)
        {
            return $"computed: {computed}";
        }

        var property = column.PropertyMappings.Select(mapping => mapping.Property).FirstOrDefault();
        if (property?.GetValueGenerationStrategy(StoreObjectIdentifier.Table(table.Name, table.Schema)) == SqlServerValueGenerationStrategy.IdentityColumn)
        {
            return "identity";
        }

        if (column.DefaultValueSql is { } sql)
        {
            return sql;
        }

        return column.DefaultValue is { } value and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    }

    private static string OnDelete(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "cascade",
        ReferentialAction.SetNull => "set null",
        ReferentialAction.SetDefault => "set default",
        ReferentialAction.Restrict => "restrict",
        _ => "no action",
    };

    private static string SqlTypeOf(Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Int64 => "bigint",
        TypeCode.Int32 => "int",
        TypeCode.Int16 => "smallint",
        TypeCode.Byte => "tinyint",
        TypeCode.Decimal => "decimal",
        _ => type.Name,
    };

    private static string ColumnList(IEnumerable<IColumn> columns) => string.Join(", ", columns.Select(column => $"`{column.Name}`"));

    // A pipe would end the cell and a line break would end the row.
    private static string Cell(string? text) => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
