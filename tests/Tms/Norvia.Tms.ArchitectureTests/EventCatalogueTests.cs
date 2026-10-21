using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The event catalogue (docs/architecture/events.md) agrees with the Canonical Model and with the subscriptions the
/// estate declares, in Bicep for Azure and in the AppHost for the local emulator. Publishers are not checked.
/// </summary>
public sealed partial class EventCatalogueTests
{
    private static readonly string Root = RepositoryRoot.Path;

    [Fact]
    public void Catalogue_EventSchemaInCanonicalModel_HasARow()
    {
        var catalogued = Catalogue().Select(row => row.Event).ToHashSet();

        var missing = EventSchemaTitles().Where(title => !catalogued.Contains(title)).ToList();

        missing.Should().BeEmpty("every event schema in contracts/canonical-model needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void Catalogue_SubscriptionInBicep_HasARow()
    {
        var missing = Missing(BicepSubscriptions());

        missing.Should().BeEmpty("every subscription in infra/estate/messaging.bicep needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void Catalogue_SubscriptionInAppHost_HasARow()
    {
        var missing = Missing(AppHostSubscriptions());

        missing.Should().BeEmpty("every subscription in the AppHost needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void Catalogue_BuiltRowNamingUnknownSubscription_Fails()
    {
        var existing = BicepSubscriptions().Intersect(AppHostSubscriptions()).ToHashSet();

        var unknown = Catalogue()
            .SelectMany(row => row.Subscriptions.Where(s => !s.Planned).Select(s => s.Key))
            .Where(key => !existing.Contains(key))
            .Distinct()
            .ToList();

        unknown.Should().BeEmpty("a row may name only subscriptions that exist in both Bicep and the AppHost, unless it marks them planned");
    }

    // The checks above pass vacuously when a parser finds nothing, so each source must yield something.
    [Fact]
    public void Catalogue_Parsed_IsNotEmpty() => Catalogue().Should().NotBeEmpty();

    [Fact]
    public void BicepSubscriptions_Parsed_IsNotEmpty() => BicepSubscriptions().Should().NotBeEmpty();

    [Fact]
    public void AppHostSubscriptions_Parsed_IsNotEmpty() => AppHostSubscriptions().Should().NotBeEmpty();

    [Fact]
    public void EventSchemaTitles_Read_IsNotEmpty() => EventSchemaTitles().Should().NotBeEmpty();

    private static List<string> Missing(IEnumerable<string> subscriptions)
    {
        var catalogued = Catalogue().SelectMany(row => row.Subscriptions.Select(s => s.Key)).ToHashSet();
        return subscriptions.Where(key => !catalogued.Contains(key)).ToList();
    }

    private static List<CatalogueRow> Catalogue()
    {
        var rows = new List<CatalogueRow>();
        string? topic = null;
        foreach (var line in File.ReadLines(Path.Combine(Root, "docs", "architecture", "events.md")))
        {
            // A topic's section starts with "### `topic`".
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                topic = Backticked(line).FirstOrDefault();
                continue;
            }

            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            // A table row is "| event | subscriptions | subscribers | process |".
            if (topic is null || cells.Length != 6 || cells[0].Length != 0 || Backticked(cells[1]).FirstOrDefault() is not { } eventName)
            {
                continue;
            }

            var subscriptions = cells[2].Split("<br/>")
                .Select(segment => (Name: Backticked(segment).FirstOrDefault(), Planned: segment.Contains("*planned*")))
                .Where(s => s.Name is not null)
                .Select(s => new CatalogueSubscription($"{topic}/{s.Name}", s.Planned))
                .ToList();
            rows.Add(new CatalogueRow(eventName, subscriptions));
        }

        return rows;
    }

    private static IEnumerable<string> Backticked(string text) =>
        BacktickedRegex().Matches(text).Select(match => match.Groups[1].Value);

    private static List<string> EventSchemaTitles()
    {
        var titles = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(Root, "contracts", "canonical-model"), "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(path));
            // An event's body names its type; commands, responses and documents do not.
            if (schema.RootElement.TryGetProperty("properties", out var properties) && properties.TryGetProperty("eventType", out _))
            {
                titles.Add(schema.RootElement.GetProperty("title").GetString() ?? throw new InvalidOperationException($"{path} has no title."));
            }
        }

        return titles;
    }

    internal static List<string> BicepSubscriptions()
    {
        var bicep = File.ReadAllText(Path.Combine(Root, "infra", "estate", "messaging.bicep"));
        var topics = BicepTopicRegex().Matches(bicep).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        return BicepSubscriptionRegex().Matches(bicep)
            .Select(m => $"{topics[m.Groups[2].Value]}/{m.Groups[3].Value}")
            .ToList();
    }

    private static List<string> AppHostSubscriptions()
    {
        var appHost = File.ReadAllText(Path.Combine(Root, "src", "Norvia.AppHost", "AppHost.cs"));
        var subscriptions = new List<string>();
        var topic = string.Empty;

        // A subscription belongs to the topic declared last above it.
        foreach (Match match in AppHostDeclarationRegex().Matches(appHost))
        {
            if (match.Groups["topic"].Success)
            {
                topic = match.Groups["topic"].Value;
            }
            else
            {
                var name = match.Groups["alias"].Success ? match.Groups["alias"].Value : match.Groups["name"].Value;
                subscriptions.Add($"{topic}/{name}");
            }
        }

        return subscriptions;
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex BacktickedRegex();

    [GeneratedRegex(@"resource (\w+) 'Microsoft\.ServiceBus/namespaces/topics@[^']+' = \{\s*parent: \w+\s*name: '([^']+)'")]
    private static partial Regex BicepTopicRegex();

    [GeneratedRegex(@"resource (\w+) 'Microsoft\.ServiceBus/namespaces/topics/subscriptions@[^']+' = \{\s*parent: (\w+)\s*name: '([^']+)'")]
    private static partial Regex BicepSubscriptionRegex();

    [GeneratedRegex("""AddServiceBusTopic\("(?<topic>[^"]+)"\)|AddServiceBusSubscription\("(?<name>[^"]+)"(?:,\s*subscriptionName:\s*"(?<alias>[^"]+)")?\)""")]
    private static partial Regex AppHostDeclarationRegex();

    private sealed record CatalogueRow(string Event, List<CatalogueSubscription> Subscriptions);

    private sealed record CatalogueSubscription(string Key, bool Planned);
}
