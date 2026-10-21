using System.Text.Json;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The event topology diagram (docs/architecture/diagrams/c4-l3-event-topology.excalidraw) draws the topics and subscriptions
/// the estate declares in Bicep, and no others. The `monitoring` subscription of each topic is not drawn. The arrows between them are not checked.
/// </summary>
public sealed class EventTopologyDiagramTests
{
    private static readonly string Root = RepositoryRoot.Path;

    [Fact]
    public void Diagram_TopicInBicep_IsDrawn()
    {
        var declared = EventCatalogueTests.BicepSubscriptions().Select(key => key.Split('/')[0]).ToHashSet();

        DrawnBefore("[topic]").Should().BeEquivalentTo(declared, "every topic in infra/estate/messaging.bicep is a node of c4-l3-event-topology; change c4.py and draw it again");
    }

    [Fact]
    public void Diagram_SubscriptionInBicep_IsDrawn()
    {
        var declared = EventCatalogueTests.BicepSubscriptions().Select(key => key.Split('/')[1]).Where(name => name != "monitoring").ToHashSet();

        DrawnBefore("[subscription").Should().BeEquivalentTo(declared, "every subscription but `monitoring` in infra/estate/messaging.bicep is a node of c4-l3-event-topology; change c4.py and draw it again");
    }

    // The diagram's text elements come in pairs: a node's title, then its subtitle, which starts with its kind in square brackets.
    private static List<string> DrawnBefore(string subtitlePrefix)
    {
        using var diagram = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "architecture", "diagrams", "c4-l3-event-topology.excalidraw")));
        var texts = diagram.RootElement.GetProperty("elements").EnumerateArray()
            .Where(element => element.GetProperty("type").GetString() == "text")
            .Select(element => element.GetProperty("text").GetString() ?? string.Empty)
            .ToList();

        var drawn = texts.Skip(1).Select((text, index) => (Text: text, Title: texts[index]))
            .Where(pair => pair.Text.StartsWith(subtitlePrefix, StringComparison.Ordinal))
            .Select(pair => pair.Title)
            .ToList();

        drawn.Should().NotBeEmpty("a diagram whose nodes can't be read would let the checks above pass for nothing");
        return drawn;
    }
}
