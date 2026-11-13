using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Application.Refusals.List;
using Norvia.IntegrationConsole.Infrastructure.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>
/// The Refused at the edge panel's query as the estate answers it: the real use case over the real Application Insights adapter, which
/// calls a Log Analytics workspace (here an in-process one) as the console's managed identity.
/// </summary>
public sealed class ApplicationInsightsRefusedCallsTests : IDisposable
{
    private const string WorkspaceId = "7f4c1b0e-0000-0000-0000-000000000001";
    private const string ConsoleToken = "console-managed-identity-token";

    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeWorkspace _workspace = new();
    private readonly RecordingCredential _identity = new(ConsoleToken);
    private readonly FakeTimeProvider _time = new(Now);

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ListAsync_FindsEachKindOfRefusal_WithTheFailureCodeItWasGiven()
    {
        _workspace
            .Request(Gateway("Submit Transport Order", "401", "key", Now.AddMinutes(-5), new() { ["Subscription Name"] = "van-dijk-transport", ["Response-X-Correlation-ID"] = "corr-key" }))
            .Request(Gateway("Submit Transport Order", "401", "token", Now.AddMinutes(-4), new() { ["azp"] = "client-van-dijk", ["Response-X-Correlation-ID"] = "corr-token" }))
            .Trace(new("token", "TokenExpired"))
            .Request(Gateway("Answer Carrier Booking", "403", "role", Now.AddMinutes(-3), new() { ["azp"] = "client-jansen", ["Response-X-Correlation-ID"] = "corr-role" }))
            .Trace(new("role", "NotAllowedToSubmit"))
            .Request(Gateway("Get Shipper Tracking", "429", "limit", Now.AddMinutes(-2), new() { ["azp"] = "client-van-dijk", ["Response-X-Correlation-ID"] = "corr-limit" }))
            .Trace(new("limit", "RateLimitExceeded"))
            .Request(Gateway("Submit Transport Order", "200", "fine", Now.AddMinutes(-1), new() { ["azp"] = "client-van-dijk" }));

        var list = await ListAsync(new ListRefusedCallsQuery());

        list.Calls.Select(call => (call.StatusCode, call.FailureCode, call.Operation, call.Caller, call.CorrelationId)).Should().Equal(
            (429, "RateLimitExceeded", "Get Shipper Tracking", "client-van-dijk", "corr-limit"),
            (403, "NotAllowedToSubmit", "Answer Carrier Booking", "client-jansen", "corr-role"),
            (401, "TokenExpired", "Submit Transport Order", "client-van-dijk", "corr-token"),
            (401, RefusalQuery.SubscriptionKeyRejected, "Submit Transport Order", "van-dijk-transport", "corr-key"));
    }

    [Fact]
    public async Task ListAsync_ForARefusalOutsideTheLastDay_LeavesItOut()
    {
        _workspace
            .Request(Gateway("Submit Transport Order", "403", "old", Now.AddHours(-25), new() { ["azp"] = "client-old" }))
            .Request(Gateway("Submit Transport Order", "403", "new", Now.AddHours(-23), new() { ["azp"] = "client-new" }));

        var list = await ListAsync(new ListRefusedCallsQuery());

        list.Calls.Should().ContainSingle().Which.Caller.Should().Be("client-new");
        var (_, _, body) = _workspace.Received.Should().ContainSingle().Subject;
        ((string)body["timespan"]!).Should().StartWith(Now.AddHours(-24).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ListAsync_ByStatus_ShowsThatStatusAndSumsItsCallers()
    {
        _workspace
            .Request(Gateway("Submit Transport Order", "403", "a", Now.AddMinutes(-9), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Submit Transport Order", "403", "b", Now.AddMinutes(-8), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Submit Transport Order", "401", "c", Now.AddMinutes(-7), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Submit Transport Order", "403", "d", Now.AddMinutes(-6), new() { ["azp"] = "client-jansen" }));

        var list = await ListAsync(new ListRefusedCallsQuery(Status: 403));

        list.Calls.Should().OnlyContain(call => call.StatusCode == 403).And.HaveCount(3);
        list.Groups.Select(group => (group.Caller, group.StatusCode, group.Count)).Should().Equal(("client-van-dijk", 403, 2), ("client-jansen", 403, 1));
    }

    [Fact]
    public async Task ListAsync_ByOperation_ShowsThatOperationAndOffersEveryOneRefused()
    {
        _workspace
            .Request(Gateway("Submit Transport Order", "403", "a", Now.AddMinutes(-9), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Get Shipper Tracking", "429", "b", Now.AddMinutes(-8), new() { ["azp"] = "client-van-dijk" }));

        var list = await ListAsync(new ListRefusedCallsQuery(Operation: "Get Shipper Tracking"));

        list.Calls.Should().ContainSingle().Which.StatusCode.Should().Be(429);
        list.Operations.Should().Equal("Get Shipper Tracking", "Submit Transport Order");
    }

    [Fact]
    public async Task ListAsync_ByCaller_GroupsTheCallerAcrossStatuses()
    {
        _workspace
            .Request(Gateway("Submit Transport Order", "403", "a", Now.AddMinutes(-9), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Get Shipper Tracking", "429", "b", Now.AddMinutes(-8), new() { ["azp"] = "client-van-dijk" }))
            .Request(Gateway("Submit Transport Order", "403", "c", Now.AddMinutes(-7), new() { ["azp"] = "client-jansen" }));

        var list = await ListAsync(new ListRefusedCallsQuery());

        list.Groups.Select(group => (group.Caller, group.StatusCode, group.Count)).Should().BeEquivalentTo(
            [("client-van-dijk", 403, 1), ("client-van-dijk", 429, 1), ("client-jansen", 403, 1)]);
    }

    [Fact]
    public async Task ListAsync_ForATelemetryRowOfAnotherGateway_LeavesItOut()
    {
        _workspace.Request(Gateway("Submit Transport Order", "403", "a", Now.AddMinutes(-9), new() { ["azp"] = "client-van-dijk" }) with { AppRoleName = "tms-api" });

        var list = await ListAsync(new ListRefusedCallsQuery());

        list.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_CallsTheWorkspaceAsTheConsolesIdentity_AndNothingElse()
    {
        _workspace.Request(Gateway("Submit Transport Order", "403", "a", Now.AddMinutes(-9), new() { ["azp"] = "client-van-dijk" }));

        await ListAsync(new ListRefusedCallsQuery());

        var (uri, authorization, _) = _workspace.Received.Should().ContainSingle().Subject;
        uri.AbsolutePath.Should().EndWith($"/workspaces/{WorkspaceId}/query");
        authorization.Should().Be($"Bearer {ConsoleToken}");
        _identity.ScopesAsked.Should().NotBeEmpty().And.OnlyContain(scopes => scopes.All(scope => scope.Contains("loganalytics", StringComparison.Ordinal) || scope.Contains("kusto", StringComparison.Ordinal) || scope.EndsWith("/.default", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ListAsync_ReadsKeysOnly_NeverABodyATokenOrASubscriptionKey()
    {
        _workspace.Request(Gateway(
            "Submit Transport Order", "401", "a", Now.AddMinutes(-9),
            new() { ["Subscription Name"] = "van-dijk-transport", ["Ocp-Apim-Subscription-Key"] = "secret-key", ["Authorization"] = "Bearer secret-token", ["Request-Body"] = "{\"secret\":1}" }));

        var list = await ListAsync(new ListRefusedCallsQuery());

        var served = System.Text.Json.JsonSerializer.Serialize(list);
        served.Should().NotContain("secret");
        RefusalQuery.Text.Should().NotContain("Ocp-Apim-Subscription-Key").And.NotContain("Authorization").And.NotContain("Body");
    }

    [Fact]
    public async Task ListAsync_WithoutAWorkspace_SaysTheConsoleIsNotConnected()
    {
        var handler = new ListRefusedCallsHandler(Adapter(workspaceId: null), _time);

        var act = async () => await handler.Handle(new ListRefusedCallsQuery(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("isn't connected to an estate");
        _workspace.Received.Should().BeEmpty();
    }

    private static GatewayRequest Gateway(string operation, string status, string operationId, DateTimeOffset time, Dictionary<string, string> properties) =>
        new(time, operation, status, operationId, properties);

    private async Task<RefusedCallList> ListAsync(ListRefusedCallsQuery query) =>
        await new ListRefusedCallsHandler(Adapter(WorkspaceId), _time).Handle(query, TestContext.Current.CancellationToken);

    private LogAnalyticsRefusedCalls Adapter(string? workspaceId) =>
        new(_workspace.Client(_identity), Options.Create(new TrailSourceOptions { LogAnalyticsWorkspaceId = workspaceId }), _time);
}
