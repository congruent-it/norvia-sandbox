using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

/// <summary>An issued invoice's lines are kept whole, as one JSON document: they are final, and the ERP only shows them.</summary>
internal static class ShipperInvoiceLinesJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static PropertyBuilder<IReadOnlyList<ShipperInvoiceLine>> AsJsonDocument(this PropertyBuilder<IReadOnlyList<ShipperInvoiceLine>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var comparer = new ValueComparer<IReadOnlyList<ShipperInvoiceLine>>(
            (left, right) => Serialize(left!) == Serialize(right!),
            lines => Serialize(lines!).GetHashCode(StringComparison.Ordinal),
            lines => Deserialize(Serialize(lines!)));

        property.HasConversion(lines => Serialize(lines), json => Deserialize(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    internal static string Serialize(IReadOnlyList<ShipperInvoiceLine> lines) => JsonSerializer.Serialize(lines, Json);

    internal static IReadOnlyList<ShipperInvoiceLine> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ShipperInvoiceLine>>(json, Json) ?? throw new JsonException("A stored invoice's lines were empty.");
}
