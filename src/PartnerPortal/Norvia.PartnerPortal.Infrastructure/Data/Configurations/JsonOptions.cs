using System.Text.Json;
using System.Text.Json.Serialization;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal static class JsonOptions
{
    /// <summary>For documents kept in a column: web casing, and enums as their names so the column stays readable.</summary>
    public static JsonSerializerOptions WithEnumNames { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
