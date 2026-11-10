using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Tms.TestSupport.Capacity;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>What the tests do as the Shipper's booking, the Planner, the Agent and its MCP server.</summary>
internal static class PlannerAgentCalls
{
    public const string Tool = "get_assignment_options";

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

    /// <summary>The Planner assigns the order, which the TMS must plan, and its ShipmentPlanned is out. Returns the Shipment number.</summary>
    public static async Task<(string ShipmentNumber, JsonNode Answer)> PlannedAsync(
        PlannerAgentFixture fixture, string number, JsonObject assignment, CancellationToken cancellationToken)
    {
        var assigned = await fixture.AssignPlannedAsync(number, assignment, cancellationToken);
        await fixture.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", cancellationToken);
        return (assigned["shipmentNumber"]!.GetValue<string>(), assigned);
    }

    /// <summary>A JSON-RPC call of the MCP protocol.</summary>
    public static Task<HttpResponseMessage> CallAsync(this HttpClient client, string method, CancellationToken cancellationToken, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri(PlannerAgentFixture.McpPath, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            cancellationToken);

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
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(PlannerAgentFixture.McpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>The Agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tool, with the Agent's token.</summary>
    public static async Task<AgentRunResult> SuggestAsync(
        PlannerAgentFixture fixture, string agentId, string transportOrderNumber, string correlationId, CancellationToken cancellationToken)
    {
        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", "planner.agent.yaml"), cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AgentToken(agentId));
        var runner = new AgentRunner(PlannerScript.Model(), new McpToolSource(http, fixture.McpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(
            definition,
            new AgentQuestion(PlannerQuestions.SuggestAssignment(transportOrderNumber), correlationId, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }
}
