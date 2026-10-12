using Microsoft.Extensions.Logging.Testing;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

internal static class ScopeKeys
{
    public static Dictionary<string, object?> Of(FakeLogRecord log) =>
        log.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
}
