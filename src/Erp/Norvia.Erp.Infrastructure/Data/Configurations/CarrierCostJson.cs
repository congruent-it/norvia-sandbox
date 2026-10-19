using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

/// <summary>A Carrier cost's charge and source are kept whole, as JSON documents: the ERP shows them and never queries into them.</summary>
internal static class CarrierCostJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static PropertyBuilder<CarrierCostCharge> AsChargeDocument(this PropertyBuilder<CarrierCostCharge> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var comparer = new ValueComparer<CarrierCostCharge>(
            (left, right) => SerializeCharge(left!) == SerializeCharge(right!),
            charge => SerializeCharge(charge!).GetHashCode(StringComparison.Ordinal),
            charge => DeserializeCharge(SerializeCharge(charge!)));

        property.HasConversion(charge => SerializeCharge(charge), json => DeserializeCharge(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    public static PropertyBuilder<CarrierCostSource> AsSourceDocument(this PropertyBuilder<CarrierCostSource> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var comparer = new ValueComparer<CarrierCostSource>(
            (left, right) => left == right,
            source => source.GetHashCode(),
            source => source);

        property.HasConversion(source => SerializeSource(source), json => DeserializeSource(json), comparer);
        return property.HasColumnType("nvarchar(max)").IsRequired();
    }

    internal static string SerializeCharge(CarrierCostCharge charge) => JsonSerializer.Serialize(charge, Json);

    internal static CarrierCostCharge DeserializeCharge(string json) =>
        JsonSerializer.Deserialize<CarrierCostCharge>(json, Json) ?? throw new JsonException("A stored Carrier cost charge was empty.");

    internal static string SerializeSource(CarrierCostSource source) =>
        JsonSerializer.Serialize(new SourceDocument(source.Kind, source.RateCardId, source.RateCardVersion, source.Remark), Json);

    internal static CarrierCostSource DeserializeSource(string json)
    {
        var document = JsonSerializer.Deserialize<SourceDocument>(json, Json) ?? throw new JsonException("A stored Carrier cost source was empty.");
        return CarrierCostSource.Restore(document.Kind, document.RateCardId, document.RateCardVersion, document.Remark);
    }

    private sealed record SourceDocument(CarrierCostSourceKind Kind, string? RateCardId, int? RateCardVersion, string? Remark);
}
