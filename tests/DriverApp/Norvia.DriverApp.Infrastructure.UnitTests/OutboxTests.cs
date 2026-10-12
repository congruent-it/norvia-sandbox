using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Infrastructure.Outbox;

namespace Norvia.DriverApp.Infrastructure.UnitTests;

/// <summary>The Driver app's outbox keeps an event until it is published, and the message it becomes carries everything the Logic App's session and tracking need (ADR 0016).</summary>
public sealed class OutboxTests
{
    private static readonly TripStartReportedV1 Event = new(
        Guid.Parse("0198e7c0-0000-7000-8000-0000000000c1"), new DateTimeOffset(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2)), "corr-1", "TR-0000012", 2, "DRV-0002");

    [Fact]
    public void TheBuffer_HoldsWhatIsAdded_UntilItIsTaken_ThenIsEmpty()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
        var buffer = new OutboxBuffer(time);

        buffer.Add(Event);
        var taken = buffer.TakePending();

        var message = taken.Should().ContainSingle().Subject;
        message.EventId.Should().Be(Event.EventId);
        message.CreatedAt.Should().Be(time.GetUtcNow());
        message.PublishedAt.Should().BeNull();
        buffer.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void TheMessage_IsTheEventsBody_OnItsTopic_InTheSessionOfItsTrip()
    {
        var message = OutboxMessage.From(Event, DateTimeOffset.UtcNow);

        message.Topic.Should().Be("driver-app-events");
        message.EventType.Should().Be("TripStartReported");
        message.SessionId.Should().Be("TR-0000012");
        message.CorrelationId.Should().Be("corr-1");
        var body = JsonNode.Parse(message.Body)!;
        body["eventId"]!.GetValue<string>().Should().Be(Event.EventId.ToString());
        body["tripNumber"]!.GetValue<string>().Should().Be("TR-0000012");
        body["driverId"]!.GetValue<string>().Should().Be("DRV-0002");
        body["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void TheMessage_KeepsTheTraceOfTheWorkThatRaisedIt()
    {
        using var source = new ActivitySource("test");
        using var listener = new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData };
        ActivitySource.AddActivityListener(listener);
        using var raising = source.StartActivity("start");

        var message = OutboxMessage.From(Event, DateTimeOffset.UtcNow);

        message.TraceParent.Should().Be(raising?.Id);
        OutboxMessage.From(Event, DateTimeOffset.UtcNow).TraceParent.Should().NotBeNull();
    }

    [Fact]
    public void TheServiceBusMessage_CarriesTheSessionTheMessageIdAndTheTrackingKeys()
    {
        var message = OutboxMessage.From(Event, DateTimeOffset.UtcNow);

        var serviceBusMessage = OutboxPublisher.ToServiceBusMessage(message);

        serviceBusMessage.MessageId.Should().Be(Event.EventId.ToString());
        serviceBusMessage.SessionId.Should().Be("TR-0000012");
        serviceBusMessage.Subject.Should().Be("TripStartReported");
        serviceBusMessage.CorrelationId.Should().Be("corr-1");
        serviceBusMessage.ContentType.Should().Be("application/json");
        serviceBusMessage.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "TripStartReported",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-1",
            ["flow"] = "trip-execution",
            ["partyId"] = "DRV-0002",
            ["tripNumber"] = "TR-0000012",
            ["sourceMessageId"] = Event.EventId.ToString(),
        });
    }

    [Fact]
    public void AMessageMarkedPublished_RemembersWhen()
    {
        var message = OutboxMessage.From(Event, DateTimeOffset.UtcNow);
        var at = new DateTimeOffset(2026, 10, 13, 6, 5, 1, TimeSpan.Zero);

        message.MarkPublished(at);

        message.PublishedAt.Should().Be(at);
    }

    [Fact]
    public void TheSpecification_TakesTheOldestUnpublishedMessagesUpToTheBatch()
    {
        var messages = Enumerable.Range(0, 3).Select(_ => OutboxMessage.From(Event, DateTimeOffset.UtcNow)).ToList();
        messages[0].MarkPublished(DateTimeOffset.UtcNow);

        new UnpublishedOutboxMessagesSpec(1).Evaluate(messages).Should().Equal(messages[1]);
        new UnpublishedOutboxMessagesSpec(5).Evaluate(messages).Should().Equal(messages[1], messages[2]);
    }
}
