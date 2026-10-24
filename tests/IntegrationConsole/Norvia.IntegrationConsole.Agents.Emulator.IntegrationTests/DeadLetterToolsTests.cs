using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// The Integration operations agent's dead-letter and failed-run tools over the Service Bus emulator: the MCP stand-in, the console's agent API
/// and the console's real Service Bus adapter, all in one process, with the emulator the AppHost runs behind it. Each test has a queue of its own and
/// messages whose correlation ID is new, so none depends on another or on the order they run in.
/// </summary>
public sealed class DeadLetterToolsTests(AgentServiceBusFixture serviceBus)
{
    private const string UnmappableTrackingMessage = "UnmappableTrackingMessage";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AgentHost NewHost() => new(consoleSettings: serviceBus.ConsoleSettings);

    // The emulator's management API reports 0 for every runtime count, so the console lists every local dead-letter queue as empty and never peeks
    // it (the same skip as the adapter's own integration tests). Unskip once the emulator reports counts or the adapter stops relying on them.
    private const string EmulatorReportsNoCounts = "The Service Bus emulator reports 0 for every runtime count (dead-letter, active and total).";

    [Fact]
    public async Task ListDeadLetterQueues_NamesEveryQueueAndSubscriptionTheEmulatorHas_AsTheAgentViewDoes()
    {
        await using var host = NewHost();
        using var client = host.StandInClient();

        var queues = await ToolAsync(client, "list_dead_letter_queues", []);

        var names = queues["queues"]!.AsArray().Select(queue => queue!["name"]!.GetValue<string>()).ToList();
        names.Should().Contain([AgentServiceBusFixture.CountsQueue, AgentServiceBusFixture.EmptyQueue, $"{AgentServiceBusFixture.CountsTopic}/{AgentServiceBusFixture.CountsSubscription}"]);
        queues["at"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        Entry(queues, AgentServiceBusFixture.EmptyQueue)["count"]!.GetValue<long>().Should().Be(0);
        Entry(queues, AgentServiceBusFixture.EmptyQueue)["oldestAgeSeconds"].Should().BeNull("an empty queue has no oldest message");
    }

    [Fact(Skip = EmulatorReportsNoCounts)]
    public async Task ListDeadLetterQueues_ReturnsTheCountAndAgeOfTheOldestMessageOfEachQueueAndSubscription()
    {
        await using var host = NewHost();
        var before = DateTimeOffset.UtcNow;
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.CountsQueue, null, "TestFailure", Emulator.Message(NewId(), "one"), Emulator.Message(NewId(), "two"), Emulator.Message(NewId(), "three"));
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.CountsTopic, AgentServiceBusFixture.CountsSubscription, "TestFailure", Emulator.Message(NewId(), "four"));
        using var client = host.StandInClient();
        var subscription = $"{AgentServiceBusFixture.CountsTopic}/{AgentServiceBusFixture.CountsSubscription}";

        var queues = await EventuallyAsync(client, "list_dead_letter_queues", view => Count(view, AgentServiceBusFixture.CountsQueue) == 3 && Count(view, subscription) == 1);

        Count(queues, AgentServiceBusFixture.CountsQueue).Should().Be(3);
        Count(queues, subscription).Should().Be(1, "a subscription is named topic/subscription");
        Count(queues, AgentServiceBusFixture.EmptyQueue).Should().Be(0);
        var oldest = Entry(queues, AgentServiceBusFixture.CountsQueue)["oldestAgeSeconds"]!.GetValue<double>();
        oldest.Should().BeInRange(0, (DateTimeOffset.UtcNow - before).TotalSeconds + 30, "the oldest message was dead-lettered during this test");
        var peeked = await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.CountsQueue, null);
        var expectedAge = (DateTimeOffset.UtcNow - peeked.Min(message => message.EnqueuedTime)).TotalSeconds;
        oldest.Should().BeLessThanOrEqualTo(expectedAge, "the age is measured from the oldest message's enqueued time");
    }

    [Fact]
    public async Task PeekDeadLetters_ReturnsEachMessagesReasonCountPropertiesAndKeys_OldestFirst()
    {
        await using var host = NewHost();
        var correlationId = NewId();
        var first = Emulator.Message(correlationId, "first body");
        var second = Emulator.Message(correlationId, "second body");
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.PeekQueue, null, UnmappableTrackingMessage, first, second);
        var direct = await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.PeekQueue, null);
        using var client = host.StandInClient();

        var view = await ToolAsync(client, "peek_dead_letters", new() { ["entity"] = AgentServiceBusFixture.PeekQueue });

        var messages = view["messages"]!.AsArray();
        messages.Select(message => message!["sequenceNumber"]!.GetValue<long>()).Should().Equal(direct.Select(message => message.SequenceNumber));
        view["hasMore"]!.GetValue<bool>().Should().BeFalse();
        var oldest = messages[0]!;
        oldest["reason"]!.GetValue<string>().Should().Be(UnmappableTrackingMessage);
        oldest["description"]!.GetValue<string>().Should().Contain(UnmappableTrackingMessage);
        oldest["deliveryCount"]!.GetValue<int>().Should().Be(direct[0].DeliveryCount);
        oldest["enqueuedAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(direct[0].EnqueuedTime, TimeSpan.FromSeconds(1));
        oldest["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        oldest["businessKeys"]!.AsArray().Select(key => key!["value"]!.GetValue<string>()).Should().Contain("SH-1000001");
        var properties = oldest["applicationProperties"]!.AsObject();
        properties["colour"]!.GetValue<string>().Should().Be("green");
        properties["correlationId"]!.GetValue<string>().Should().Be(correlationId);
    }

    [Fact]
    public async Task ListAndPeek_LeaveEveryDeadLetterQueueUnchanged()
    {
        await using var host = NewHost();
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.UnchangedQueue, null, "TestFailure", Emulator.Message(NewId(), "one"), Emulator.Message(NewId(), "two"));
        var before = Snapshot(await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.UnchangedQueue, null));
        using var client = host.StandInClient();

        for (var round = 0; round < 2; round++)
        {
            await ToolAsync(client, "list_dead_letter_queues", []);
            await ToolAsync(client, "peek_dead_letters", new() { ["entity"] = AgentServiceBusFixture.UnchangedQueue });
        }

        Snapshot(await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.UnchangedQueue, null)).Should().Equal(before, "a peek keeps each message's delivery count and place");
        var received = await Emulator.ReceiveDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.UnchangedQueue, null, 2);
        received.Select(message => message.SequenceNumber).Should().Equal(before.Select(message => message.Sequence), "no message was left locked by the peek");
    }

    [Fact]
    public async Task NoTool_ReturnsAMessageBody()
    {
        await using var host = NewHost();
        var body = "BODY-" + Guid.NewGuid().ToString("N");
        var correlationId = NewId();
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.BodiesQueue, null, "TestFailure", Emulator.Message(correlationId, body));
        host.FailedRuns.Add(new RecordedFailedRun(null, DateTimeOffset.UtcNow.AddMinutes(-5), "carrier-tracking", correlationId, "Map", new StepError("Boom", "Mapping failed.")));
        using var client = host.StandInClient();

        var texts = new List<string>
        {
            (await ToolAsync(client, "list_failed_runs", [])).ToJsonString(),
            (await ToolAsync(client, "list_dead_letter_queues", [])).ToJsonString(),
            (await ToolAsync(client, "peek_dead_letters", new() { ["entity"] = AgentServiceBusFixture.BodiesQueue })).ToJsonString(),
        };

        texts.Should().OnlyContain(text => !text.Contains(body, StringComparison.Ordinal));
        var peek = JsonNode.Parse(texts[2])!;
        PropertyNames(peek).Should().BeSubsetOf(AllowedDeadLetterProperties);
        var direct = (await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.BodiesQueue, null)).Single();
        direct.Body.ToString().Should().Be(body, "the message does carry a body, which the tools leave out");
    }

    [Fact]
    public async Task ListFailedRuns_ReturnsTheFailedRunsOfTheLastDay_NewestFirst()
    {
        await using var host = NewHost();
        var now = DateTimeOffset.UtcNow;
        var recent = NewId();
        var older = NewId();
        var tooOld = NewId();
        host.FailedRuns.Add(new RecordedFailedRun(null, now.AddHours(-20), "carrier-tracking", older, "Receive", new StepError("Timeout", "No answer.")));
        host.FailedRuns.Add(new RecordedFailedRun(null, now.AddHours(-30), "carrier-tracking", tooOld, "Map", new StepError("Old", "Yesterday's.")));
        host.FailedRuns.Add(new RecordedFailedRun(null, now.AddMinutes(-10), "carrier-tracking", recent, "Map", new StepError(UnmappableTrackingMessage, "Status code DLV is not in the map.")));
        using var client = host.StandInClient();

        var view = await ToolAsync(client, "list_failed_runs", []);

        var runs = view["runs"]!.AsArray();
        runs.Select(run => run!["correlationId"]!.GetValue<string>()).Should().Equal(recent, older);
        var newest = runs[0]!;
        newest["flow"]!.GetValue<string>().Should().Be("carrier-tracking");
        newest["failedStep"]!.GetValue<string>().Should().Be("Map");
        newest["error"]!["code"]!.GetValue<string>().Should().Be(UnmappableTrackingMessage);
        newest["error"]!["message"]!.GetValue<string>().Should().Be("Status code DLV is not in the map.");
        newest["start"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(now.AddMinutes(-10), TimeSpan.FromSeconds(1));
        (view["to"]!.GetValue<DateTimeOffset>() - view["from"]!.GetValue<DateTimeOffset>()).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task ExplainingADeadLetteredPod_RendersTheScriptedAnswer_FromThePeekOfTheEmulatorsQueue()
    {
        await using var host = NewHost();
        var correlationId = NewId();
        await Emulator.DeadLetterAsync(serviceBus.Client, AgentServiceBusFixture.ExplainQueue, null, UnmappableTrackingMessage, Emulator.Message(correlationId, "{\"status\":\"DLV\"}"));
        var sequence = (await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.ExplainQueue, null)).Single().SequenceNumber;

        using var scope = host.ConsoleServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ExplainDeadLetterQuery(AgentServiceBusFixture.ExplainQueue, sequence, correlationId), Ct);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Cause.Should().Contain("could not map the status code");
        result.Value.SuggestedAction.Should().Contain("POD").And.Contain("DLV");
        result.Value.Evidence.Should().Contain(evidence => evidence.ToolName == "peek_dead_letters" && evidence.Claim.Contains(UnmappableTrackingMessage, StringComparison.Ordinal));
    }

    private static readonly HashSet<string> AllowedDeadLetterProperties =
    [
        "entity", "messages", "hasMore", "sequenceNumber", "enqueuedAt", "reason", "description", "deliveryCount", "correlationId", "businessKeys",
        "kind", "value", "applicationProperties", "shipmentNumber", "colour", "DeadLetterReason", "DeadLetterErrorDescription",
    ];

    private static string NewId() => Guid.NewGuid().ToString();

    private static List<(long Sequence, int Deliveries)> Snapshot(IReadOnlyList<Azure.Messaging.ServiceBus.ServiceBusReceivedMessage> messages) =>
        [.. messages.Select(message => (message.SequenceNumber, message.DeliveryCount))];

    private static JsonObject Entry(JsonNode view, string name) =>
        view["queues"]!.AsArray().Select(queue => queue!.AsObject()).Single(queue => queue["name"]!.GetValue<string>() == name);

    private static long Count(JsonNode view, string name) =>
        view["queues"]!.AsArray().FirstOrDefault(queue => queue!["name"]!.GetValue<string>() == name)?["count"]!.GetValue<long>() ?? -1;

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };

    /// <summary>The emulator's runtime counts trail what it has just done, so a tool is called again until its answer shows it.</summary>
    private static Task<JsonNode> EventuallyAsync(HttpClient client, string tool, Func<JsonNode, bool> done) =>
        Emulator.EventuallyAsync(() => ToolAsync(client, tool, []), done);

    /// <summary>Calls a tool and returns the JSON the agent view answered with.</summary>
    private static async Task<JsonNode> ToolAsync(HttpClient client, string tool, Dictionary<string, string> arguments)
    {
        var parameters = new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = new JsonObject(arguments.Select(argument => KeyValuePair.Create(argument.Key, (JsonNode?)JsonValue.Create(argument.Value)))),
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/mcp", UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = parameters },
            Ct);
        response.EnsureSuccessStatusCode();
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse(reply.ToJsonString());
        return JsonNode.Parse(reply["content"]![0]!["text"]!.GetValue<string>())!;
    }
}
