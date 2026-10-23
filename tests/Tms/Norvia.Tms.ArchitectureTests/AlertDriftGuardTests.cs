using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The drift guard of the estate's alerts (docs/operations/as-found.md): every Logic App and every Service Bus queue and
/// subscription declared in infra/estate has an alert, or an explicit exemption with its reason in
/// infra/estate/alert-exemptions.json, so a new flow cannot be added unalerted by accident. The Bicep is read as text, like
/// <see cref="FailureBehaviourTests"/> does.
/// </summary>
public sealed partial class AlertDriftGuardTests
{
    private static readonly string Root = RepositoryRoot.Path;

    [Fact]
    public void EveryLogicApp_HasAFailedRunAlert_OrAnExemption()
    {
        var alerted = AlertedLogicApps();
        var exempt = Exemptions("logicApps");

        var unalerted = LogicAppModules().Where(module => !alerted.Contains(module) && !exempt.ContainsKey(module)).ToList();

        unalerted.Should().BeEmpty("a Logic App needs an entry in the alerts module of infra/estate.bicep, or an exemption with a reason in infra/estate/alert-exemptions.json");
    }

    [Fact]
    public void EveryQueueAndSubscription_HasADeadLetterAlert_OrAnExemption()
    {
        var alerted = AlertedEntities();
        var exempt = Exemptions("serviceBusEntities");

        var unalerted = DeclaredEntities().Where(entity => !alerted.Contains(entity.Name) && !exempt.ContainsKey(entity.Key)).Select(entity => entity.Key).ToList();

        unalerted.Should().BeEmpty("a queue or subscription needs an entity in the alerts module of infra/estate.bicep, or an exemption with a reason in infra/estate/alert-exemptions.json");
    }

    [Fact]
    public void EveryAlert_IsForSomethingTheEstateDeclares()
    {
        var workflows = AlertedWorkflows();
        var declaredWorkflows = Directory.GetDirectories(Path.Combine(Root, "src", "Integration"), "*LogicApp").SelectMany(Directory.GetDirectories)
            .Where(directory => File.Exists(Path.Combine(directory, "workflow.json"))).Select(Path.GetFileName).ToList();

        workflows.Should().BeSubsetOf(declaredWorkflows, "an alert for a workflow no Logic App holds would never fire");
        AlertedEntities().Should().BeSubsetOf(DeclaredEntities().Select(entity => entity.Name));
    }

    [Fact]
    public void AlertedSubscriptions_HaveNamesNoOtherEntityShares()
    {
        // The metric's EntityName is the subscription's name, so two entities of one name could not be told apart.
        var exempt = Exemptions("serviceBusEntities");

        var shared = DeclaredEntities().Where(entity => !exempt.ContainsKey(entity.Key)).GroupBy(entity => entity.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        shared.Should().BeEmpty();
    }

    [Fact]
    public void Exemptions_NameSomethingDeclared_AndGiveAReason()
    {
        var declaredEntities = DeclaredEntities().Select(entity => entity.Key).ToList();
        var logicApps = Exemptions("logicApps");
        var entities = Exemptions("serviceBusEntities");

        logicApps.Keys.Should().BeSubsetOf(LogicAppModules(), "an exemption for a Logic App that is gone is stale");
        entities.Keys.Should().BeSubsetOf(declaredEntities, "an exemption for an entity that is gone is stale");
        logicApps.Concat(entities).Should().OnlyContain(exemption => !string.IsNullOrWhiteSpace(exemption.Value));
    }

    [Fact]
    public void AsFoundPreset_LeavesSeveralFlowsAndEntitiesUnalerted_ButNotAll()
    {
        var preset = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", "as-found.json")))!;
        var flows = preset["flowsWithoutFailedRunAlert"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        var entities = preset["entitiesWithoutDeadLetterAlert"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();

        flows.Count.Should().BeInRange(2, AlertedWorkflows().Count - 1);
        entities.Count.Should().BeInRange(2, AlertedEntities().Count - 1);
        flows.Should().BeSubsetOf(AlertedWorkflows(), "a toggle can only list what the estate alerts");
        entities.Should().BeSubsetOf(AlertedEntities());
    }

    // The guard passes vacuously when a parser finds nothing, so each source must yield something.
    [Fact]
    public void Sources_Parsed_AreNotEmpty()
    {
        LogicAppModules().Should().NotBeEmpty();
        AlertedLogicApps().Should().NotBeEmpty();
        AlertedWorkflows().Should().NotBeEmpty();
        DeclaredEntities().Should().NotBeEmpty();
        AlertedEntities().Should().NotBeEmpty();
    }

    private sealed record Entity(string Key, string Name);

    private static string Estate() => File.ReadAllText(Path.Combine(Root, "infra", "estate.bicep"));

    // The alerts module of estate.bicep, which lists the Logic Apps and entities that are alerted.
    private static string AlertsModule()
    {
        var estate = Estate();
        var start = estate.IndexOf("module alerts ", StringComparison.Ordinal);
        return estate[start..estate.IndexOf("\nmodule messaging ", start, StringComparison.Ordinal)];
    }

    // A Logic App is declared by its module file; the module name in estate.bicep stands for it.
    private static List<string> LogicAppModules() =>
        Directory.GetFiles(Path.Combine(Root, "infra", "estate"), "*.bicep")
            .Where(file => File.ReadAllText(file).Contains("kind: 'functionapp,workflowapp'", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .Select(file => ModuleRegex().Matches(Estate()).Single(match => match.Groups[2].Value == file).Groups[1].Value)
            .ToList();

    private static HashSet<string> AlertedLogicApps() =>
        LogicAppNameRegex().Matches(AlertsModule()).Select(match => match.Groups[1].Value).ToHashSet();

    private static HashSet<string> AlertedWorkflows() =>
        WorkflowNameRegex().Matches(AlertsModule()).Select(match => match.Groups[1].Value).ToHashSet();

    private static HashSet<string> AlertedEntities()
    {
        var module = AlertsModule();
        var list = module[module.IndexOf("entities: [", StringComparison.Ordinal)..];
        return QuotedRegex().Matches(list[..list.IndexOf(']')]).Select(match => match.Groups[1].Value).ToHashSet();
    }

    // Queues, and subscriptions keyed by their topic as well: `topic/subscription`.
    private static List<Entity> DeclaredEntities()
    {
        var bicep = string.Concat(Directory.GetFiles(Path.Combine(Root, "infra", "estate"), "*.bicep").Select(File.ReadAllText));
        var topics = TopicRegex().Matches(bicep).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        var queues = QueueRegex().Matches(bicep).Select(match => new Entity(match.Groups[1].Value, match.Groups[1].Value));
        var subscriptions = SubscriptionRegex().Matches(bicep)
            .Select(match => new Entity($"{topics[match.Groups[1].Value]}/{match.Groups[2].Value}", match.Groups[2].Value));
        return queues.Concat(subscriptions).ToList();
    }

    private static Dictionary<string, string> Exemptions(string kind) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "estate", "alert-exemptions.json")))![kind]!.AsObject()
            .ToDictionary(exemption => exemption.Key, exemption => exemption.Value!.GetValue<string>());

    [GeneratedRegex(@"^module (\w+) 'estate/([\w-]+\.bicep)'", RegexOptions.Multiline)]
    private static partial Regex ModuleRegex();

    [GeneratedRegex(@"logicAppName: (\w+)\.outputs\.logicAppName")]
    private static partial Regex LogicAppNameRegex();

    [GeneratedRegex(@"workflowName: '([^']+)'")]
    private static partial Regex WorkflowNameRegex();

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex QuotedRegex();

    [GeneratedRegex(@"resource (\w+) 'Microsoft\.ServiceBus/namespaces/topics@[^']+' = \{\s*parent: \w+\s*name: '([^']+)'")]
    private static partial Regex TopicRegex();

    [GeneratedRegex(@"resource \w+ 'Microsoft\.ServiceBus/namespaces/queues@[^']+' = \{\s*parent: \w+\s*name: '([^']+)'")]
    private static partial Regex QueueRegex();

    [GeneratedRegex(@"resource \w+ 'Microsoft\.ServiceBus/namespaces/topics/subscriptions@[^']+' = \{\s*parent: (\w+)\s*name: '([^']+)'")]
    private static partial Regex SubscriptionRegex();
}
