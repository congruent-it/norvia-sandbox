using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalDriverReportsWorker.UnitTests;

public class DriverReportsConsumerTests
{
    private DriverReportsWorld _world = new();
    private readonly ServiceBusSessionReceiver _receiver = Substitute.For<ServiceBusSessionReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new DriverReportsConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, NullLogger<DriverReportsConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body), sessionId: DriverReportsWorld.TripNumber);

        await consumer.HandleAsync(new ProcessSessionMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ARecordedReport_CompletesTheMessage_BeforeTheOutcomeIsReported()
    {
        _receiver.When(receiver => receiver.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>()))
            .Do(_ => _world.DriverApp.Requests.Should().BeEmpty("the message is completed first"));

        await HandleAsync(DriverReportsWorld.StartReported());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        _world.DriverApp.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_ARefusedReport_IsDeadLetteredWithTheTmsCodeAndMessage_AndNotCompleted()
    {
        _world.Tms.Otherwise(System.Net.HttpStatusCode.Conflict, DriverReportsWorld.Problem(409, "NotTripDay", "Wrong day."));

        await HandleAsync(DriverReportsWorld.StartReported());

        await _receiver.Received(1).DeadLetterMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), "NotTripDay", "Wrong day.", Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AnUnreadableMessage_IsDeadLetteredAsUnreadable()
    {
        await HandleAsync("not json");

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "DriverReportUnreadable", Arg.Is<string>(text => text.Contains("nothing was sent to the TMS")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ARunWhoseOutcomeCannotBeReported_StillLeavesTheMessageSettled()
    {
        // No retries, so the test needn't move the clock the retry waits on.
        _world = new DriverReportsWorld(retryCount: 0);
        _world.DriverApp.Otherwise(System.Net.HttpStatusCode.ServiceUnavailable);

        await HandleAsync(DriverReportsWorld.StartReported());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheSessionProcessor_ReadsOneTripsReportsOneAtATime_AndSettlesThemItself()
    {
        DriverReportsConsumer.ProcessorOptions.MaxConcurrentCallsPerSession.Should().Be(1, "one Trip's reports are processed one at a time, in order");
        DriverReportsConsumer.ProcessorOptions.AutoCompleteMessages.Should().BeFalse();
        DriverReportsConsumer.ProcessorOptions.MaxConcurrentSessions.Should().BeGreaterThan(1, "different Trips are processed side by side");
    }
}

public class DriverReportsConsumerDisabledTests
{
    [Fact]
    public void IsDisabled_OnlyWhileTheFlagFileExists()
    {
        var flag = Path.Combine(Path.GetTempPath(), $"norvia-flag-{Guid.NewGuid():N}");
        var options = new DriverReportsWorkerOptions { DisabledFlagPath = flag };
        try
        {
            DriverReportsConsumer.IsDisabled(options).Should().BeFalse();
            File.WriteAllText(flag, string.Empty);
            DriverReportsConsumer.IsDisabled(options).Should().BeTrue();
        }
        finally
        {
            File.Delete(flag);
        }

        DriverReportsConsumer.IsDisabled(options).Should().BeFalse();
        DriverReportsConsumer.IsDisabled(new DriverReportsWorkerOptions()).Should().BeFalse("no flag is configured");
    }
}
