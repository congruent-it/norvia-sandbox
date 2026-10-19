using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

/// <summary>A Carrier invoice's lines are kept whole, as one JSON document: the ERP shows them and never queries into them.</summary>
internal static class CarrierInvoiceLinesJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static PropertyBuilder<IReadOnlyList<CarrierInvoiceLine>> AsLinesDocument(this PropertyBuilder<IReadOnlyList<CarrierInvoiceLine>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var comparer = new ValueComparer<IReadOnlyList<CarrierInvoiceLine>>(
            (left, right) => Serialize(left!) == Serialize(right!),
            lines => Serialize(lines!).GetHashCode(StringComparison.Ordinal),
            lines => Deserialize(Serialize(lines!)));

        property.HasConversion(lines => Serialize(lines), json => Deserialize(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    internal static string Serialize(IReadOnlyList<CarrierInvoiceLine> lines) => JsonSerializer.Serialize(lines, Json);

    internal static IReadOnlyList<CarrierInvoiceLine> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<CarrierInvoiceLine>>(json, Json) ?? throw new JsonException("A stored Carrier invoice's lines were empty.");
}
