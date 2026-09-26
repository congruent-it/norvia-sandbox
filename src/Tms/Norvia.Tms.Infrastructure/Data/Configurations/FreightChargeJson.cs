using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

/// <summary>
/// How a frozen Freight Charge is stored: amounts as plain numbers, the Rate Card ID as a plain string, and charge line
/// types by name, so the column reads like the Canonical Model's Freight Charge.
/// </summary>
internal static class FreightChargeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new MoneyAsNumber(), new RateCardIdAsString() },
    };

    private sealed class MoneyAsNumber : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetDecimal());

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Amount);
    }

    private sealed class RateCardIdAsString : JsonConverter<RateCardId>
    {
        public override RateCardId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? throw new JsonException("A Rate Card ID can't be null."));

        public override void Write(Utf8JsonWriter writer, RateCardId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
