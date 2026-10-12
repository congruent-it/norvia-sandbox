using System.Text.Json;

namespace Norvia.DriverApp.Infrastructure.Outbox;

internal static class DriverAppEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
