using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class DeadLetterMessagesTests : ConsoleWebContext
{
    private const string Entity = "shipment-events/carrier-booking";
    private const string Booking = """{"consignee":{"name":"Café De Kroon"}}""";

    // 12:00 in Rotterdam (CEST).
    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();
    private readonly FakeLogger<DeadLetterMessages> _log = new();

    public DeadLetterMessagesTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<ILogger<DeadLetterMessages>>(_log);
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Answer(
            Listed(4, Enqueued.AddMinutes(5), "MaxDeliveryCountExceeded", "Failed 10 times", 10, "corr-2", new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0043")),
            Listed(3, Enqueued, "MappingFailed", null, 2, "corr-1", new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042"), new BusinessKey(BusinessKeyKind.TripNumber, "TR-7")));
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        typeof(DeadLetterMessages).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ForAQueue_ListsItsMessages_AsTheyAreGiven_WithTheFieldsButNoBody()
    {
        var page = RenderMessages();

        _deadLetters.Received(1).ListMessagesAsync(new ListDeadLetteredMessagesQuery(Entity), Arg.Any<CancellationToken>());
        page.Find("h1").TextContent.Should().Contain(Entity);
        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("Sat 26 Sep 2026, 12:05:00").And.Contain("MaxDeliveryCountExceeded").And.Contain("Failed 10 times")
            .And.Contain("10").And.Contain("corr-2").And.Contain("Shipment SH-0043");
        rows[1].TextContent.Should().Contain("MappingFailed").And.Contain("corr-1").And.Contain("Shipment SH-0042").And.Contain("Trip TR-7");
        page.Markup.Should().NotContain("Café").And.NotContain("consignee");
        page.FindAll("pre").Should().BeEmpty();
        _deadLetters.DidNotReceiveWithAnyArgs().OpenMessageAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Render_ForAMessage_LinksItsCorrelationIdToItsTrail_AndItsKeysToASearch()
    {
        var page = RenderMessages();

        var row = page.FindAll("tbody tr")[1];
        row.QuerySelectorAll("a").Select(link => link.GetAttribute("href")).Should().Contain(
            ["/trails/corr-1", "/?key=SH-0042&within=all", "/?key=TR-7&within=all"]);
    }

    [Fact]
    public void Render_WhenTheQueueHoldsMoreThanThePage_SaysSo()
    {
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new DeadLetteredMessageList(Entity, [Listed(3, Enqueued, "MappingFailed", null, 2, "corr-1")], HasMore: true));

        var page = RenderMessages();

        page.Markup.Should().Contain("Showing the oldest 1 messages. The queue holds more.");
    }

    [Fact]
    public void Render_WithoutMessages_SaysSo()
    {
        Answer();

        var page = RenderMessages();

        page.Find("[role=status]").TextContent.Should().Contain("has no dead-lettered messages");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<Result<DeadLetteredMessageList>>().Task);

        var page = RenderMessages();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the dead-lettered messages");
    }

    [Fact]
    public void Render_WhenTheMessagesCantBeListed_SaysSo_WithoutTheReason_AndLogsIt()
    {
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<DeadLetteredMessageList>>(_ => throw new HttpRequestException("403 AuthorizationFailed"));

        var page = RenderMessages();

        page.Find("[role=alert]").TextContent.Should().Contain("couldn't be listed");
        page.Markup.Should().NotContain("AuthorizationFailed");
        _log.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Error && record.Message.Contains(Entity));
    }

    [Fact]
    public void Render_ForAQueueThatDoesNotExist_SaysSo()
    {
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeadLetteredMessageList>.NotFound());

        var page = RenderMessages();

        page.Find("[role=status]").TextContent.Should().Contain("no queue or subscription named " + Entity);
    }

    [Fact]
    public void Render_WhenTheRequestIsInvalid_SaysWhy()
    {
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeadLetteredMessageList>.Invalid(new ValidationError("Entity", "Open a dead-letter queue by its queue or subscription name.")));

        var page = RenderMessages();

        page.Find("[role=alert]").TextContent.Should().Contain("Open a dead-letter queue by its queue or subscription name.");
    }

    [Fact]
    public void OpeningAMessage_FetchesIt_AndShowsItsBodyAndApplicationProperties()
    {
        AnswerOpen(3, Booking, new Dictionary<string, string> { ["shipmentNumber"] = "SH-0042", ["flow"] = "carrier-booking" });
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        _deadLetters.Received(1).OpenMessageAsync(new OpenDeadLetteredMessageQuery(Entity, 3), Arg.Any<CancellationToken>());
        var message = page.Find("[aria-label='Message 3']");
        message.QuerySelector("pre")!.TextContent.Should().Be("""
            {
              "consignee": {
                "name": "Café De Kroon"
              }
            }
            """);
        var properties = message.QuerySelectorAll("[data-properties] dt, [data-properties] dd").Select(element => element.TextContent).ToList();
        properties.Should().Equal("flow", "carrier-booking", "shipmentNumber", "SH-0042");
    }

    [Fact]
    public void OpeningAMessage_WithoutApplicationProperties_SaysSo()
    {
        AnswerOpen(3, "plain text", new Dictionary<string, string>());
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        var message = page.Find("[aria-label='Message 3']");
        message.QuerySelector("pre")!.TextContent.Should().Be("plain text");
        message.TextContent.Should().Contain("No application properties.");
    }

    [Fact]
    public void OpeningAMessage_WhileItIsFetched_SaysSo()
    {
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<Result<RecordedDeadLetterBody>>().Task);
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        page.Find("[aria-label='Message 3'] [role=status]").TextContent.Should().Contain("Fetching message 3");
    }

    [Fact]
    public void OpeningAMessage_ThatIsGone_SaysItWasResubmittedOrDiscarded()
    {
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<RecordedDeadLetterBody>.NotFound());
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        page.Find("[aria-label='Message 3'] [role=status]").TextContent.Should().Contain("no longer on the queue");
        page.FindAll("pre").Should().BeEmpty();
    }

    [Fact]
    public void OpeningAMessage_WhenTheRequestIsInvalid_SaysWhy()
    {
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<RecordedDeadLetterBody>.Invalid(new ValidationError("Entity", "Open a message by its queue or subscription and its sequence number.")));
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        page.Find("[aria-label='Message 3'] [role=alert]").TextContent.Should().Contain("Open a message by its queue or subscription and its sequence number.");
    }

    [Fact]
    public void OpeningAMessage_WhenItCantBeFetched_SaysSo_WithoutTheReason_AndLogsNoBody()
    {
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<RecordedDeadLetterBody>>(_ => throw new InvalidOperationException(Booking));
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        page.Find("[aria-label='Message 3'] [role=alert]").TextContent.Should().Contain("couldn't be opened");
        page.Markup.Should().NotContain("Café");
        var record = _log.Collector.GetSnapshot().Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        record.Message.Should().Contain("3").And.Contain(Entity).And.NotContain("Café");
    }

    [Fact]
    public void OpeningAMessage_Always_KeepsTheBodyAndPropertiesOutOfTheLogs()
    {
        AnswerOpen(3, Booking, new Dictionary<string, string> { ["consignee"] = "Café De Kroon" });
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();

        page.Find("[aria-label='Message 3'] pre").TextContent.Should().Contain("Café De Kroon");
        _log.Collector.GetSnapshot().Should().OnlyContain(record => !record.Message.Contains("Café") && !record.Message.Contains("consignee"));
    }

    [Fact]
    public void OpeningAnotherMessage_ReplacesTheFirst()
    {
        AnswerOpen(3, Booking, new Dictionary<string, string>());
        AnswerOpen(4, """{"other":true}""", new Dictionary<string, string>());
        var page = RenderMessages();

        page.Find("fluent-button[data-message='3']").Click();
        page.Find("fluent-button[data-message='4']").Click();

        page.FindAll("section[aria-label^='Message']").Should().ContainSingle().Which.GetAttribute("aria-label").Should().Be("Message 4");
        page.Find("pre").TextContent.Should().Contain("other");
    }

    [Fact]
    public void Links_ForASubscription_EscapeEachPartOfItsName()
    {
        DeadLetterLinks.Messages("shipment-events/carrier booking").Should().Be("/dead-letter-queues/shipment-events/carrier%20booking");
        DeadLetterLinks.Messages("carrier-tracking").Should().Be("/dead-letter-queues/carrier-tracking");
    }

    [Theory]
    [InlineData(null, "–")]
    [InlineData(" ", "–")]
    [InlineData("MaxDeliveryCountExceeded", "MaxDeliveryCountExceeded")]
    public void Reason_Always_ShowsADashWhenThereIsNone(string? reason, string expected)
    {
        DeadLetterText.Reason(reason).Should().Be(expected);
    }

    [Fact]
    public void Render_ForALinkedMessage_OpensIt()
    {
        AnswerOpen(3, Booking, new Dictionary<string, string>());

        var page = RenderLinked(3);

        _deadLetters.Received(1).OpenMessageAsync(new OpenDeadLetteredMessageQuery(Entity, 3), Arg.Any<CancellationToken>());
        page.Find("[data-message=3]").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Render_ForALinkedMessageNotInTheList_OpensNothing()
    {
        RenderLinked(99);

        _deadLetters.DidNotReceiveWithAnyArgs().OpenMessageAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    private IRenderedComponent<DeadLetterMessages> RenderLinked(long sequenceNumber)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("message", sequenceNumber));
        return RenderMessages();
    }

    private IRenderedComponent<DeadLetterMessages> RenderMessages() =>
        Render<DeadLetterMessages>(parameters => parameters.Add(page => page.Entity, Entity));

    private static RecordedDeadLetter Listed(long sequenceNumber, DateTimeOffset enqueued, string? reason, string? description, int deliveries, string? correlationId, params BusinessKey[] keys) =>
        new(sequenceNumber, enqueued, reason, description, deliveries, correlationId, keys);

    private void Answer(params RecordedDeadLetter[] messages) =>
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new DeadLetteredMessageList(Entity, messages, HasMore: false));

    private void AnswerOpen(long sequenceNumber, string body, IReadOnlyDictionary<string, string> properties) =>
        _deadLetters.OpenMessageAsync(new OpenDeadLetteredMessageQuery(Entity, sequenceNumber), Arg.Any<CancellationToken>())
            .Returns(new RecordedDeadLetterBody(Listed(sequenceNumber, Enqueued, null, null, 1, null), body, properties));
}
