using System.Text.Json;

namespace Norvia.Erp.Infrastructure.Outbox;

internal static class IntegrationEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
