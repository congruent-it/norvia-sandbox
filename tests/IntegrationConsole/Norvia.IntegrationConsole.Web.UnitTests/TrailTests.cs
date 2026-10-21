using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class TrailTests : ConsoleWebContext
{
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    // 12:00 in Rotterdam (CEST).
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly ITrailRequests _trails = Substitute.For<ITrailRequests>();

    public TrailTests()
    {
        Services.AddSingleton(_trails);
        Answer();
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        typeof(Trail).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ForACorrelationId_OpensItsTrail()
    {
        var page = RenderTrail();

        _trails.Received(1).OpenAsync(new OpenTrailQuery(CorrelationId), Arg.Any<CancellationToken>());
        page.Find("h1").TextContent.Should().Contain("Message trail").And.Contain(CorrelationId);
    }

    [Fact]
    public void Render_WithHops_ShowsEachWithItsFlowTimeDurationOutcomeAndFailureCode()
    {
        Answer(
            Hop("apim-norvia: Submit transport order", seconds: 0, TimeSpan.FromMilliseconds(250), Outcome.Succeeded),
            Hop("logic-intake: transport-order-intake", seconds: 1, TimeSpan.FromSeconds(1.5), Outcome.Failed, "TmsUnavailable"),
            Hop("tms-api", seconds: 2, null, null));

        var page = RenderTrail();

        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("apim-norvia: Submit transport order").And.Contain("transport-order-intake")
            .And.Contain("Sat 26 Sep 2026, 12:00:00").And.Contain("250 ms").And.Contain("Succeeded");
        rows[1].TextContent.Should().Contain("Sat 26 Sep 2026, 12:00:01").And.Contain("1.5 s").And.Contain("Failed").And.Contain("TmsUnavailable");
        rows[2].TextContent.Should().Contain("No outcome recorded");
        page.Find("tbody tr [data-outcome=Failed]").TextContent.Trim().Should().Be("Failed");
    }

    [Fact]
    public void Render_WithHops_ShowsKeysOnly_NeverABody()
    {
        // Bodies hold personal data; they show only when an operator opens a step of a run.
        Answer(Hop("tms-api", seconds: 0, null, Outcome.Succeeded));

        var page = RenderTrail();

        page.FindAll("thead th").Select(header => header.TextContent.Trim())
            .Should().Equal("Hop", "Flow", "Time", "Duration", "Outcome", "Failure code");
    }

    [Fact]
    public void Render_WithAFanOut_IndentsEachBranchUnderTheHopThatCausedIt()
    {
        Answer(
            Hop("tms-api: publish TransportOrderAccepted", seconds: 0, null, Outcome.Succeeded),
            Hop("logic-billing", seconds: 1, null, Outcome.Succeeded, depth: 1),
            Hop("logic-wms", seconds: 2, null, Outcome.Succeeded, depth: 1));

        var page = RenderTrail();

        page.FindAll("tbody tr [data-depth]").Select(hop => (hop.TextContent.Trim(), hop.GetAttribute("data-depth")))
            .Should().Equal(("tms-api: publish TransportOrderAccepted", "0"), ("logic-billing", "1"), ("logic-wms", "1"));
    }

    [Fact]
    public void Render_WithBusinessKeys_LinksEachToASearchForIt_OverEverythingKept()
    {
        // So the operator follows a Shipment across trails: trails don't chain, business keys join them.
        Answer(
            [Hop("tms-api", seconds: 0, null, Outcome.Succeeded)],
            [new(BusinessKeyKind.TransportOrderNumber, "TO-0000042"), new(BusinessKeyKind.ShipmentNumber, "SH 42/A")]);

        var page = RenderTrail();

        page.FindAll("[aria-label='Business keys'] a").Select(link => (link.TextContent.Trim(), link.GetAttribute("href"))).Should().Equal(
            ("Transport Order TO-0000042", "/?key=TO-0000042&within=all"),
            ("Shipment SH 42/A", "/?key=SH%2042%2FA&within=all"));
    }

    [Fact]
    public void Render_WithALogicAppRun_LinksToItsRunDetail()
    {
        Answer(Hop("logic-intake: transport-order-intake", seconds: 0, null, Outcome.Succeeded) with
        {
            Run = new WorkflowRun("logic-intake-abc", "transport-order-intake", "08584421797283920101CU00"),
        });

        var page = RenderTrail();

        page.Find("tbody a").GetAttribute("href").Should().Be("/runs/logic-intake-abc/transport-order-intake/08584421797283920101CU00");
    }

    [Fact]
    public void Render_Always_SaysTelemetryMayBeAFewMinutesBehind()
    {
        Answer(Hop("tms-api", seconds: 0, null, Outcome.Succeeded));

        var page = RenderTrail();

        page.Find("[role=note]").TextContent.Should().Contain("Telemetry may be a few minutes behind");
    }

    [Fact]
    public void Render_WhileOpening_SaysSo_AndThatTelemetryMayLag()
    {
        _trails.OpenAsync(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<MessageTrail>>().Task);

        var page = RenderTrail();

        page.Find("[role=status]").TextContent.Should().Contain("Opening the Message trail");
        page.Find("[role=note]").TextContent.Should().Contain("Telemetry may be a few minutes behind");
    }

    [Fact]
    public void Render_WhenNothingIsRecordedYet_SaysSo()
    {
        var page = RenderTrail();

        page.Find("[role=status]").TextContent.Should().Contain($"Nothing is recorded under {CorrelationId} yet.");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheTrailCantBeOpened_SaysSo()
    {
        _trails.OpenAsync(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<MessageTrail>>(_ => throw new InvalidOperationException("Log Analytics is down."));

        var page = RenderTrail();

        page.Find("[role=alert]").TextContent.Should().Contain("The Message trail couldn't be opened.");
        page.Markup.Should().NotContain("Log Analytics is down.");
    }

    [Fact]
    public void Render_WhenTheCorrelationIdIsInvalid_SaysWhy()
    {
        _trails.OpenAsync(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<MessageTrail>.Invalid(new ValidationError("CorrelationId", "Open a Message trail by its correlation ID.")));

        var page = RenderTrail(" ");

        page.Find("[role=alert]").TextContent.Should().Contain("Open a Message trail by its correlation ID.");
    }

    [Fact]
    public void Render_ForAHopStillOnTheQueue_LinksToItsMessage()
    {
        Answer(Hop("logic-intake", seconds: 0, null, Outcome.DeadLettered) with { DeadLetter = new DeadLetterHandling(DeadLetterStatus.OnQueue, "shipment-events/carrier-booking", 7, null, null, null) });

        var page = RenderTrail();

        var link = page.Find("[data-dead-letter=onqueue] a");
        link.GetAttribute("href").Should().Be("/dead-letter-queues/shipment-events/carrier-booking?message=7");
        link.TextContent.Should().Be("Open in the dead-letter queue");
    }

    [Fact]
    public void Render_ForAResubmittedHop_SaysWhoAndWhen_WithoutALink()
    {
        Answer(Hop("logic-intake", seconds: 0, null, Outcome.DeadLettered) with { DeadLetter = new DeadLetterHandling(DeadLetterStatus.Resubmitted, "q", null, "ana@norvia.example", Now, null) });

        var page = RenderTrail();

        page.Find("[data-dead-letter=resubmitted]").TextContent.Trim().Should().Be("Resubmitted by ana@norvia.example on Sat 26 Sep 2026, 12:00:00");
        page.FindAll("[data-dead-letter] a").Should().BeEmpty();
    }

    [Fact]
    public void Render_ForADiscardedHop_SaysWhoWhenAndWhy()
    {
        Answer(Hop("logic-intake", seconds: 0, null, Outcome.DeadLettered) with { DeadLetter = new DeadLetterHandling(DeadLetterStatus.Discarded, "q", null, "ben@norvia.example", Now, "Duplicate") });

        var page = RenderTrail();

        page.Find("[data-dead-letter=discarded]").TextContent.Trim().Should().Be("Discarded by ben@norvia.example on Sat 26 Sep 2026, 12:00:00: Duplicate");
    }

    [Fact]
    public void Render_ForHopsWithoutADeadLetter_ShowsNoHandling()
    {
        Answer(Hop("tms-api", seconds: 0, null, Outcome.Succeeded));

        RenderTrail().FindAll("[data-dead-letter]").Should().BeEmpty();
    }

    private IRenderedComponent<Trail> RenderTrail(string correlationId = CorrelationId) =>
        Render<Trail>(parameters => parameters.Add(page => page.CorrelationId, correlationId));

    private static TrailHop Hop(string name, int seconds, TimeSpan? duration, Outcome? outcome, string? failureCode = null, int depth = 0) =>
        new(name, "transport-order-intake", Now.AddSeconds(seconds), duration, outcome, failureCode, depth, null);

    private void Answer(params TrailHop[] hops) => Answer(hops, []);

    private void Answer(TrailHop[] hops, BusinessKey[] keys) =>
        _trails.OpenAsync(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new MessageTrail(CorrelationId, Now.AddDays(-90), Now, hops, keys)));
}
