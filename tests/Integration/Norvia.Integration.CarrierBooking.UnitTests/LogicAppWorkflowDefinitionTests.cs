using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// The Logic Apps runtime validates a workflow definition when it loads it, and a workflow it refuses is Unhealthy and never runs. The runtime only exists in
/// Azure, so these tests check the two mistakes it has refused so far in every workflow definition.
/// </summary>
public sealed partial class LogicAppWorkflowDefinitionTests
{
    public static TheoryData<string> WorkflowPaths { get; } = [.. Contracts.Workflows()];

    // The Service Bus built-in connector's operations that have no unsuffixed ID any more (docs: Service Bus built-in connector operations).
    private static readonly string[] RetiredOperationIds = ["deadLetterTopicMessage"];

    [Fact]
    public void Workflows_Embedded_IsNotEmpty() => Contracts.Workflows().Should().NotBeEmpty();

    [Theory]
    [MemberData(nameof(WorkflowPaths))]
    public void TrackedProperties_ReadOnlyTheirOwnActionTheTriggerAndParameters(string path)
    {
        var others = Operations(path)
            .SelectMany(operation => operation.Node["trackedProperties"]?.AsObject() ?? [], (operation, property) => (operation.Name, property.Key, Expression: property.Value!.ToString()))
            .SelectMany(tracked => ReadActionRegex().Matches(tracked.Expression).Select(match => (tracked.Name, tracked.Key, Reads: match.Groups[1].Value)))
            .Where(read => read.Reads != read.Name)
            .ToList();

        others.Should().BeEmpty("a tracked property may only read its own action's inputs and outputs, the trigger and parameters, or the run is refused at load");
    }

    [Theory]
    [MemberData(nameof(WorkflowPaths))]
    public void ServiceBusOperations_AreNotOnesTheRuntimeHasRetired(string path)
    {
        var used = Operations(path)
            .Select(operation => (operation.Node["inputs"] as JsonObject)?["serviceProviderConfiguration"]?["operationId"]?.GetValue<string>())
            .OfType<string>();

        used.Should().NotContain(RetiredOperationIds);
    }

    private static IEnumerable<(string Name, JsonNode Node)> Operations(string path) =>
        Walk(JsonNode.Parse(Contracts.Read(path))!["definition"]!.AsObject());

    private static IEnumerable<(string Name, JsonNode Node)> Walk(JsonObject scope)
    {
        var triggers = scope["triggers"]?.AsObject() ?? [];
        foreach (var trigger in triggers)
        {
            yield return (trigger.Key, trigger.Value!);
        }

        foreach (var operation in Nested(scope["actions"]?.AsObject()))
        {
            yield return operation;
        }
    }

    private static IEnumerable<(string Name, JsonNode Node)> Nested(JsonObject? actions)
    {
        foreach (var (name, node) in actions ?? [])
        {
            yield return (name, node!);
            foreach (var child in new[] { node!["actions"], node["else"]?["actions"], node["default"]?["actions"] }.OfType<JsonObject>().Concat(node["cases"]?.AsObject().Select(@case => @case.Value!["actions"]).OfType<JsonObject>() ?? []))
            {
                foreach (var operation in Nested(child))
                {
                    yield return operation;
                }
            }
        }
    }

    [GeneratedRegex(@"(?:actions|outputs|body|result)\('([^']+)'\)")]
    private static partial Regex ReadActionRegex();
}
