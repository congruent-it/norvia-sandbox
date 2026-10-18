using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

/// <summary>A Freight Charge is kept whole, as one JSON document: the ERP shows it and never queries into it or recalculates it.</summary>
internal static class FreightChargeJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static PropertyBuilder<FreightCharge> AsJsonDocument(this PropertyBuilder<FreightCharge> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var comparer = new ValueComparer<FreightCharge>(
            (left, right) => Serialize(left!) == Serialize(right!),
            charge => Serialize(charge!).GetHashCode(StringComparison.Ordinal),
            charge => Deserialize(Serialize(charge!)));

        property.HasConversion(charge => Serialize(charge), json => Deserialize(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    internal static string Serialize(FreightCharge charge) => JsonSerializer.Serialize(charge, Json);

    internal static FreightCharge Deserialize(string json) =>
        JsonSerializer.Deserialize<FreightCharge>(json, Json) ?? throw new JsonException("A stored Freight Charge was empty.");
}
