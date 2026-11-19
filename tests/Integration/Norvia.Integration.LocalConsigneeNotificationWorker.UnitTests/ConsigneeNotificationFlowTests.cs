using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalConsigneeNotificationWorker.UnitTests;

public class ConsigneeNotificationFlowTests
{
    [Fact]
    public async Task RunAsync_TheTmsGivesAnAddressAndACode_SendsTheEmailWithTheLinkAndIsDone()
    {
        var world = new ConsigneeNotificationWorld();

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.Done);
        var email = world.Mailbox.Sent.Should().ContainSingle().Subject;
        email["to"]!.GetValue<string>().Should().Be(ConsigneeNotificationWorld.Email);
        email["plainText"]!.GetValue<string>().Should().Contain("https://portal.norvia.example/track/7QK2M9XH4D8VWRBT");
    }

    [Fact]
    public async Task RunAsync_AskingTheTms_UsesTheTransportOrderTheCorrelationIdAndTheReadRole()
    {
        var world = new ConsigneeNotificationWorld();

        await world.RunAsync(ConsigneeNotificationWorld.Request());

        var call = world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.AbsolutePath.Should().Be("/v1/consignee-notifications/TO-0000005");
        call.Method.Should().Be(HttpMethod.Get);
        call.Headers["X-Correlation-ID"].Should().Be("collected-5");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(call.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("ConsigneeNotifications.Read");
        token.GetClaim("azp").Value.Should().Be("consignee-notification-worker");
    }

    [Theory]
    [InlineData("Collected", "collected")]
    [InlineData("DeliveryWindowAgreed", "delivery window")]
    public async Task RunAsync_EachReason_SaysSo(string reason, string words)
    {
        var world = new ConsigneeNotificationWorld();

        await world.RunAsync(ConsigneeNotificationWorld.Request(reason));

        world.Mailbox.Sent.Single()["plainText"]!.GetValue<string>().Should().Contain(words);
    }

    [Fact]
    public async Task RunAsync_ATransportOrderWithNothingToSend_SendsNothingAndIsNoError()
    {
        var world = new ConsigneeNotificationWorld();
        world.Tms.Otherwise(HttpStatusCode.NotFound);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.Done);
        world.Mailbox.Sent.Should().BeEmpty();
        world.Tms.Requests.Should().ContainSingle("a 404 is final and is not retried");
    }

    [Fact]
    public async Task RunAsync_TheTmsFailsThenAnswers_RetriesWithGrowingWaits()
    {
        var world = new ConsigneeNotificationWorld();
        world.Tms.Then(HttpStatusCode.InternalServerError).Then(HttpStatusCode.BadGateway);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.Done);
        var times = world.Tms.Requests.Select(request => request.At).ToList();
        times.Should().HaveCount(3);
        (times[1] - times[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        (times[2] - times[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        world.Mailbox.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "TmsUnavailable")]
    [InlineData(HttpStatusCode.Forbidden, "TmsRefused")]
    [InlineData(HttpStatusCode.BadRequest, "TmsRefused")]
    public async Task RunAsync_TheTmsFailsForGood_SaysWhetherToTryAgain_AndSendsNothing(HttpStatusCode status, string expected)
    {
        var world = new ConsigneeNotificationWorld(retryCount: 2);
        world.Tms.Otherwise(status);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.ToString().Should().Be(expected);
        world.Mailbox.Sent.Should().BeEmpty();
        world.Tms.Requests.Should().HaveCount(expected == "TmsUnavailable" ? 3 : 1);
    }

    [Fact]
    public async Task RunAsync_TheTmsCantBeReached_IsUnavailable()
    {
        var world = new ConsigneeNotificationWorld(retryCount: 0);
        world.Tms.ThenFail(new HttpRequestException("Connection refused"));

        (await world.RunAsync(ConsigneeNotificationWorld.Request())).Should().Be(ConsigneeNotificationFlowOutcome.TmsUnavailable);
    }

    [Fact]
    public async Task RunAsync_ATmsTimeout_IsRetried()
    {
        var world = new ConsigneeNotificationWorld();
        world.Tms.ThenFail(new TaskCanceledException("timed out"));

        (await world.RunAsync(ConsigneeNotificationWorld.Request())).Should().Be(ConsigneeNotificationFlowOutcome.Done);
        world.Tms.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("""{"transportOrderNumber":"TO-0000005","email":"two@@x.example","trackingCode":"7QK2M9XH4D8VWRBT"}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000005","email":"inkoop@dezwaan.example","trackingCode":"short"}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000099","email":"inkoop@dezwaan.example","trackingCode":"7QK2M9XH4D8VWRBT"}""")]
    [InlineData("not json")]
    public async Task RunAsync_ATmsAnswerThatCantBeUsed_SendsNothing(string body)
    {
        var world = new ConsigneeNotificationWorld();
        world.Tms.Otherwise(HttpStatusCode.OK, body);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.EmailUnusable);
        world.Mailbox.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TheMailServerIsBusyThenTakesTheEmail_RetriesAndIsDone()
    {
        var world = new ConsigneeNotificationWorld();
        world.Mailbox.Then(new(false, true)).Then(new(false, true));

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.Done);
        world.Mailbox.Sent.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_TheMailServerIsDownEveryTime_StopsAfterTheBound()
    {
        var world = new ConsigneeNotificationWorld(retryCount: 3);
        world.Mailbox.Otherwise = new(false, true);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.EmailNotSent);
        world.Mailbox.Sent.Should().HaveCount(4);
    }

    [Fact]
    public async Task RunAsync_TheMailServerRefusesTheEmail_IsNotRetried()
    {
        var world = new ConsigneeNotificationWorld();
        world.Mailbox.Otherwise = new(false, false);

        var outcome = await world.RunAsync(ConsigneeNotificationWorld.Request());

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.EmailNotSent);
        world.Mailbox.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""["a notification"]""")]
    [InlineData("""{ "correlationId": "c-1", "reason": "Collected" }""")]
    [InlineData("""{ "transportOrderNumber": "TO-0000005", "reason": "Delivered" }""")]
    public async Task RunAsync_NotANotificationTheConsigneeCanBeToldOf_IsUnreadableAndCallsNobody(string message)
    {
        var world = new ConsigneeNotificationWorld();

        var outcome = await world.RunAsync(message);

        outcome.Should().Be(ConsigneeNotificationFlowOutcome.Unreadable);
        world.Tms.Requests.Should().BeEmpty();
        world.Mailbox.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_AMessageWithoutACorrelationId_StillRunsOnTheEmptyOne()
    {
        var world = new ConsigneeNotificationWorld();

        await world.RunAsync("""{ "transportOrderNumber": "TO-0000005", "reason": "Collected" }""");

        world.Tms.Requests.Single().Headers["X-Correlation-ID"].Should().Be("00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public async Task RunAsync_NeverLogsTheAddressOrTheCode_InAnyOutcome()
    {
        var world = new ConsigneeNotificationWorld(retryCount: 1);
        world.Tms.Then(HttpStatusCode.ServiceUnavailable);
        world.Mailbox.Then(new(false, true));

        await world.RunAsync(ConsigneeNotificationWorld.Request());

        var everything = string.Join('\n', world.Logger.Collector.GetSnapshot().Select(record => record.Message + " " + string.Join(' ', record.Scopes.Select(scope => scope?.ToString() ?? string.Empty))));
        everything.Should().NotBeEmpty().And.NotContain("dezwaan").And.NotContain(ConsigneeNotificationWorld.Code);
        world.Logger.Collector.GetSnapshot().SelectMany(record => record.Scopes).Select(scope => scope?.ToString()).Should().NotContain(text => text!.Contains("dezwaan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_TheTrail_CarriesTheContractsKeysAndNoParty()
    {
        var world = new ConsigneeNotificationWorld();

        await world.RunAsync(ConsigneeNotificationWorld.Request());

        var keys = world.Logger.Collector.GetSnapshot().SelectMany(record => record.Scopes).OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToList();
        keys.Should().Contain(new KeyValuePair<string, object?>("flow", "consignee-notification"))
            .And.Contain(new KeyValuePair<string, object?>("correlationId", "collected-5"))
            .And.Contain(new KeyValuePair<string, object?>("transportOrderNumber", "TO-0000005"))
            .And.Contain(new KeyValuePair<string, object?>("shipmentNumber", "SH-0000045"))
            .And.Contain(new KeyValuePair<string, object?>("sourceMessageId", "7d1e0c4a-5b2f-4e8a-9c3d-0a1b2c3d4e05"));
        keys.Select(key => key.Key).Should().NotContain("partyId");
    }

    [Fact]
    public void TmsAnswer_NeverPrintsItsBody() =>
        new TmsAnswer(200, ConsigneeNotificationWorld.Content).ToString().Should().NotContain("dezwaan").And.NotContain(ConsigneeNotificationWorld.Code);
}
