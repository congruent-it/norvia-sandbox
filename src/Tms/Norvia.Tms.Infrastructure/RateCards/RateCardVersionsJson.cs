using System.Text.Json;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>A replicated card keeps its versions as the plain values the ERP's event carried; they are mapped to the domain's when read.</summary>
internal static class RateCardVersionsJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<RateCardVersionData> versions) => JsonSerializer.Serialize(versions, Json);

    public static IReadOnlyList<RateCardVersion> Read(string json) =>
        RateCardVersionMapper.ToDomain(JsonSerializer.Deserialize<List<RateCardVersionData>>(json, Json) ?? []);
}
