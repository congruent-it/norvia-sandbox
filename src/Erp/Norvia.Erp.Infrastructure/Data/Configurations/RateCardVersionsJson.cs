using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

/// <summary>A card's versions are kept whole, as one JSON document: the ERP shows them and never queries into them.</summary>
internal static class RateCardVersionsJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<IReadOnlyList<RateCardVersion>> AsJsonDocument(this PropertyBuilder<IReadOnlyList<RateCardVersion>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        // Compared by their JSON, so reloading the same seed twice changes nothing and saves nothing.
        var comparer = new ValueComparer<IReadOnlyList<RateCardVersion>>(
            (left, right) => Serialize(left) == Serialize(right),
            versions => Serialize(versions).GetHashCode(StringComparison.Ordinal),
            versions => Deserialize(Serialize(versions)));

        property.HasConversion(versions => Serialize(versions), json => Deserialize(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    internal static string Serialize(IReadOnlyList<RateCardVersion>? versions) => JsonSerializer.Serialize(versions ?? [], Json);

    internal static IReadOnlyList<RateCardVersion> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<RateCardVersion>>(json, Json) ?? [];
}
