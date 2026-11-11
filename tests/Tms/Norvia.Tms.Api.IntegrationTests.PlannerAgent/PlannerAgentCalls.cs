using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Tms.TestSupport.Capacity;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>What the tests do as the Shipper's booking, the Planner, the Agent and its MCP server.</summary>
internal static class PlannerAgentCalls
{
    public const string Tool = "get_assignment_options";
    public const string TimelineTool = "get_exception_timeline";

    /// <summary>The flow the TMS API's agent view traces its calls under, and the stand-in's.</summary>
    public const string AgentViewFlow = "agent-view-planner";
    public const string StandInFlow = "agent-mcp-planner";

    /// <summary>What <see cref="Bookings.Valid"/> holds beyond a city, a postcode area and a country, none of which the Agent may read.</summary>
    public static readonly string[] NeverForTheAgent =
    [
        "Café De Zwaan", "Sanne", "+31 30 555 0142", "Oudegracht", "3511 AW", "3511AW", "3511", "Waalhaven Oostzijde", "3087 BM", "3087BM", "3087", "Ring at the side entrance",
        "consignee", "street", "instructions",
    ];

    /// <summary>
    /// The nth weekday after today in Norvia's time zone, within the Planning horizon. Own fleet has Drivers on weekdays, and a test that assigns to Own fleet
    /// uses a day of its own, so no other test's assignment changes the day's load it checks.
    /// </summary>
    public static DateOnly Weekday(int n)
    {
        var day = CapacityCalls.Today(DateTimeOffset.UtcNow);
        for (var found = -1; found < n;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                found++;
            }
        }

        return day;
    }

    /// <summary>A booking of the test's own, collected on the day at ten past eight in Norvia's time zone and delivered the same day.</summary>
    public static JsonObject BookingOn(DateOnly day) =>
        Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(CapacityCalls.ShortlyAfterMidnightOf(day).AddHours(8));

    /// <summary>Submits the booking, which the TMS accepts, and waits until rating has settled, so it does not change the order while the test reads it.</summary>
    public static async Task<string> AcceptedAsync(PlannerAgentFixture fixture, JsonObject booking, CancellationToken cancellationToken)
    {
        var number = await fixture.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), cancellationToken);
        await fixture.WaitForRatingToSettleAsync(number, cancellationToken);
        return number;
    }

    /// <summary>The Planner assigns the order, which the TMS must plan, and its ShipmentPlanned is out. Returns the TMS's answer: the Shipment number, the correlation ID and any load warning.</summary>
    public static async Task<JsonNode> PlannedAsync(
        PlannerAgentFixture fixture, string number, JsonObject assignment, CancellationToken cancellationToken)
    {
        var assigned = await fixture.AssignPlannedAsync(number, assignment, cancellationToken);
        await fixture.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", cancellationToken);
        return assigned;
    }

    /// <summary>The assignment as the TMS UI sends it when the Planner acts on the Agent's Suggestion: it names the Agent and its run.</summary>
    public static JsonObject ActingOn(this JsonObject assignment, string agentName, string agentRunId)
    {
        assignment["agentName"] = agentName;
        assignment["agentRunId"] = agentRunId;
        return assignment;
    }

    /// <summary>A JSON-RPC call of the MCP protocol.</summary>
    public static Task<HttpResponseMessage> CallAsync(this HttpClient client, string method, CancellationToken cancellationToken) =>
        client.SendAsync(Request(method, new JsonObject(), correlationId: null), cancellationToken);

    private static HttpRequestMessage Request(string method, JsonObject parameters, string? correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(PlannerAgentFixture.McpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters }),
        };
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return request;
    }

    /// <summary>A <c>tools/call</c> of the MCP protocol, whatever the stand-in answers.</summary>
    public static Task<HttpResponseMessage> CallToolRawAsync(this HttpClient client, string tool, JsonObject arguments, CancellationToken cancellationToken) =>
        client.SendAsync(Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments }, Guid.NewGuid().ToString()), cancellationToken);

    /// <summary>Calls <c>get_assignment_options</c> and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static Task<(bool IsError, string Text)> GetAssignmentOptionsAsync(this HttpClient client, string transportOrderNumber, CancellationToken cancellationToken) =>
        client.CallToolAsync(Tool, new JsonObject { ["transportOrderNumber"] = transportOrderNumber }, Guid.NewGuid().ToString(), cancellationToken);

    /// <summary>The assignment options of the order, as the Agent reads them, which must be a success.</summary>
    public static async Task<JsonNode> OptionsOfAsync(this HttpClient client, string transportOrderNumber, CancellationToken cancellationToken)
    {
        var (isError, text) = await client.GetAssignmentOptionsAsync(transportOrderNumber, cancellationToken);
        isError.Should().BeFalse(text);
        return JsonNode.Parse(text)!;
    }

    /// <summary>The Carrier's option among the options' carriers.</summary>
    public static JsonNode CarrierOf(this JsonNode options, string carrierId) =>
        options["carriers"]!.AsArray().Single(carrier => carrier!["carrierId"]!.GetValue<string>() == carrierId)!;

    /// <summary>Calls a tool and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static async Task<(bool IsError, string Text)> CallToolAsync(
        this HttpClient client, string tool, JsonObject arguments, string correlationId, CancellationToken cancellationToken)
    {
        using var request = Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments }, correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>The Agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tool, with the Agent's token.</summary>
    public static Task<AgentRunResult> SuggestAsync(
        PlannerAgentFixture fixture, string agentId, string transportOrderNumber, string correlationId, CancellationToken cancellationToken) =>
        RunAsync(fixture, agentId, "planner.agent.yaml", PlannerScript.Model(), PlannerQuestions.SuggestAssignment(transportOrderNumber), correlationId, cancellationToken);

    /// <summary>Calls <c>get_exception_timeline</c> and returns the text the tool answered with, which must be a success.</summary>
    public static async Task<string> TimelineTextOfAsync(this HttpClient client, string exceptionNumber, CancellationToken cancellationToken)
    {
        var (isError, text) = await client.CallToolAsync(TimelineTool, new JsonObject { ["exceptionNumber"] = exceptionNumber }, Guid.NewGuid().ToString(), cancellationToken);
        isError.Should().BeFalse(text);
        return text;
    }

    /// <summary>What a timeline holds beyond the Consignee, street and postcode values of <see cref="NeverForTheAgent"/>: the Tracking event's own postcode too. Bare digits are left out, as IDs may hold them.</summary>
    public static readonly string[] NeverInATimeline =
    [
        .. NeverForTheAgent.Where(never => never.Length > 4 && never is not ("street" or "instructions" or "consignee")), "3011 AA", "3011AA",
    ];

    internal static async Task<AgentRunResult> RunAsync(
        PlannerAgentFixture fixture, string agentId, string definitionFile, IChatClient model, string question, string correlationId, CancellationToken cancellationToken)
    {
        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", definitionFile), cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AgentToken(agentId));
        var runner = new AgentRunner(model, new McpToolSource(http, fixture.McpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(definition, new AgentQuestion(question, correlationId, Guid.NewGuid().ToString("N")), cancellationToken);
    }
}
