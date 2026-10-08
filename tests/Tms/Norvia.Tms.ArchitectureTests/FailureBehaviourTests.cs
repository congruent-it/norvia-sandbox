using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The failure behaviour page (docs/architecture/failure-behaviour.md) names every failure code, Flow, queue and
/// subscription the estate declares. What each row says about it is not checked.
/// </summary>
public sealed partial class FailureBehaviourTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Page_FailureCodeInPolicyOrWorkflow_IsNamed()
    {
        var missing = FailureCodes().Where(code => !Page().Contains(code)).ToList();

        missing.Should().BeEmpty("every failure code in an APIM policy or a Logic App needs a row in docs/architecture/failure-behaviour.md");
    }

    [Fact]
    public void Page_FlowInPolicyOrWorkflow_IsNamed()
    {
        var missing = Flows().Where(flow => !Page().Contains(flow)).ToList();

        missing.Should().BeEmpty("every Flow in an APIM policy or a Logic App needs a row in docs/architecture/failure-behaviour.md");
    }

    [Fact]
    public void Page_QueueOrSubscriptionInBicep_IsNamed()
    {
        var missing = QueuesAndSubscriptions().Where(name => !Page().Contains(name)).ToList();

        missing.Should().BeEmpty("every queue and subscription in infra/estate/messaging.bicep needs a row in docs/architecture/failure-behaviour.md");
    }

    // The checks above pass vacuously when a parser finds nothing, so each source must yield something.
    [Fact]
    public void FailureCodes_Parsed_IsNotEmpty() => FailureCodes().Should().NotBeEmpty();

    [Fact]
    public void Flows_Parsed_IsNotEmpty() => Flows().Should().NotBeEmpty();

    [Fact]
    public void QueuesAndSubscriptions_Parsed_IsNotEmpty() => QueuesAndSubscriptions().Should().NotBeEmpty();

    [Fact]
    public void Page_Parsed_IsNotEmpty() => Page().Should().NotBeEmpty();

    private static HashSet<string> Page() =>
        BacktickedRegex().Matches(File.ReadAllText(Path.Combine(Root, "docs", "architecture", "failure-behaviour.md")))
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

    // A code is a quoted PascalCase literal on a line that sets a failure code, a run error code or a dead-letter reason.
    private static List<string> FailureCodes() =>
        EstateFiles()
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("failureCode") || line.Contains("\"code\":") || line.Contains("deadLetterReason"))
            .SelectMany(line => CodeLiteralRegex().Matches(line).Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();

    private static List<string> Flows() =>
        EstateFiles()
            .SelectMany(File.ReadLines)
            .Select(line => FlowRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

    private static List<string> QueuesAndSubscriptions()
    {
        var bicep = File.ReadAllText(Path.Combine(Root, "infra", "estate", "messaging.bicep"));
        return BicepEntityRegex().Matches(bicep).Select(match => match.Groups[1].Value).Distinct().ToList();
    }

    private static IEnumerable<string> EstateFiles() =>
        Directory.GetFiles(Path.Combine(Root, "infra", "estate", "partner-api"), "*.policy.xml")
            .Concat(Directory.GetFiles(Path.Combine(Root, "src", "Integration"), "workflow.json", SearchOption.AllDirectories));

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

    [GeneratedRegex("""(?:&quot;|'|")([A-Z][A-Za-z]+)(?:&quot;|'|")""")]
    private static partial Regex CodeLiteralRegex();

    [GeneratedRegex(@"(?:name=""flow"" value=|""flow"":\s*)""([a-z-]+)""")]
    private static partial Regex FlowRegex();

    [GeneratedRegex(@"resource \w+ 'Microsoft\.ServiceBus/namespaces/(?:queues|topics/subscriptions)@[^']+' = \{\s*parent: \w+\s*name: '([^']+)'")]
    private static partial Regex BicepEntityRegex();
}
