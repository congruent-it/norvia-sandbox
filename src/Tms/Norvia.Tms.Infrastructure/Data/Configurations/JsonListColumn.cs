using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

/// <summary>
/// A list an aggregate reads and writes only with itself, stored as one JSON column: the conversion and the comparer that
/// lets EF see a changed list.
/// </summary>
internal static class JsonListColumn
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The items themselves are what the JSON holds.</summary>
    public static PropertyBuilder<IReadOnlyList<TItem>> HasJsonList<TItem>(this PropertyBuilder<IReadOnlyList<TItem>> property) =>
        property.HasJsonList(item => item, stored => stored);

    /// <summary>The JSON holds <typeparamref name="TStored"/> values, plain ones, that the items convert to and from.</summary>
    public static PropertyBuilder<IReadOnlyList<TItem>> HasJsonList<TItem, TStored>(
        this PropertyBuilder<IReadOnlyList<TItem>> property, Func<TItem, TStored> toStored, Func<TStored, TItem> toItem)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.HasConversion(
            items => JsonSerializer.Serialize(items.Select(toStored), Json),
            json => JsonSerializer.Deserialize<TStored[]>(json, Json)!.Select(toItem).ToArray(),
            new ValueComparer<IReadOnlyList<TItem>>(
                (left, right) => left!.SequenceEqual(right!),
                items => items.Count,
                items => items.ToArray()));
    }
}
