using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// The Logic Apps runtime validates a workflow definition when it loads it, and a workflow it refuses is Unhealthy and never runs. The runtime only exists in
/// Azure, so these tests check the mistakes it has refused so far in every workflow definition.
/// </summary>
public sealed partial class LogicAppWorkflowDefinitionTests
{
    public static TheoryData<string> WorkflowPaths { get; } = [.. Contracts.Workflows()];

    // The Service Bus built-in connector's operations that have no unsuffixed ID any more (docs: Service Bus built-in connector operations).
    private static readonly string[] RetiredOperationIds = ["deadLetterTopicMessage", "deadLetterQueueMessage", "completeTopicMessage"];

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
    public void TrackedProperties_InAWorkflowWithSecureData_ReadOnlyTheirOwnInputsAndOutputs(string path)
    {
        var operations = Operations(path).ToList();
        var secure = operations.Where(operation => operation.Node["runtimeConfiguration"]?["secureData"] is not null).Select(operation => operation.Name).ToList();
        if (secure.Count == 0)
        {
            return;
        }

        // The runtime refuses the workflow when a step with tracked properties reads the trigger, or reads a step that has secureData in its own definition.
        var offenders = operations
            .Where(operation => operation.Node["trackedProperties"] is JsonObject { Count: > 0 })
            .SelectMany(operation => operation.Node["trackedProperties"]!.AsObject()
                .Select(property => (operation.Name, Key: property.Key, Reason: TriggerReadRegex().IsMatch(property.Value!.ToString()) ? "reads the trigger" : null))
                .Append((operation.Name, Key: "(inputs)", Reason: secure.Any(name => operation.Name != name && operation.Node["inputs"]?.ToJsonString().Contains($"('{name}')", StringComparison.Ordinal) == true) ? "reads a secureData step" : null)))
            .Where(offence => offence.Reason is not null)
            .ToList();

        offenders.Should().BeEmpty("with secureData in the workflow, a step with tracked properties may read only its own inputs and outputs, and not a secureData step, or the run is refused at load");
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

    [GeneratedRegex(@"trigger(?:Body|Outputs)?\(")]
    private static partial Regex TriggerReadRegex();
}
