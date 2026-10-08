using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The event catalogue (docs/architecture/events.md) agrees with the Canonical Model and with the subscriptions the
/// estate declares, in Bicep for Azure and in the AppHost for the local emulator. Publishers are not checked.
/// </summary>
public sealed partial class EventCatalogueTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void EveryEventSchema_HasARow()
    {
        var catalogued = Catalogue().Select(row => row.Event).ToHashSet();

        var missing = EventSchemaTitles().Where(title => !catalogued.Contains(title)).ToList();

        missing.Should().BeEmpty("every event schema in contracts/canonical-model needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void EverySubscriptionInBicep_HasARow()
    {
        var missing = Missing(BicepSubscriptions());

        missing.Should().BeEmpty("every subscription in infra/estate/messaging.bicep needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void EverySubscriptionInTheAppHost_HasARow()
    {
        var missing = Missing(AppHostSubscriptions());

        missing.Should().BeEmpty("every subscription in the AppHost needs a row in docs/architecture/events.md");
    }

    [Fact]
    public void NoBuiltRow_NamesASubscriptionThatDoesNotExist()
    {
        var existing = BicepSubscriptions().Union(AppHostSubscriptions()).ToHashSet();

        var unknown = Catalogue()
            .SelectMany(row => row.Subscriptions.Where(s => !s.Planned).Select(s => s.Key))
            .Where(key => !existing.Contains(key))
            .Distinct()
            .ToList();

        unknown.Should().BeEmpty("a row may name only subscriptions that exist, unless it marks them planned");
    }

    [Fact]
    public void TheCatalogue_IsNotEmpty()
    {
        Catalogue().Should().NotBeEmpty();
        BicepSubscriptions().Should().NotBeEmpty();
        AppHostSubscriptions().Should().NotBeEmpty();
        EventSchemaTitles().Should().NotBeEmpty();
    }

    private static List<string> Missing(IEnumerable<string> subscriptions)
    {
        var catalogued = Catalogue().SelectMany(row => row.Subscriptions.Select(s => s.Key)).ToHashSet();
        return subscriptions.Where(key => !catalogued.Contains(key)).ToList();
    }

    private static List<CatalogueRow> Catalogue()
    {
        var rows = new List<CatalogueRow>();
        foreach (var line in File.ReadLines(Path.Combine(Root, "docs", "architecture", "events.md")))
        {
            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            // A table row is "| event | topic | publisher | subscriptions | subscribers | process |".
            if (cells.Length != 8 || cells[0].Length != 0 || Backticked(cells[1]).FirstOrDefault() is not { } eventName)
            {
                continue;
            }

            var topic = Backticked(cells[2]).FirstOrDefault();
            if (topic is null)
            {
                continue;
            }

            var subscriptions = cells[4].Split("<br/>")
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
                titles.Add(schema.RootElement.GetProperty("title").GetString()!);
            }
        }

        return titles;
    }

    private static List<string> BicepSubscriptions()
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Found no Norvia.slnx above the test assembly.");
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
