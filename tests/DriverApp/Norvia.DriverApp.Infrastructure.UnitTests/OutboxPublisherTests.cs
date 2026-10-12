using Ardalis.SharedKernel;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Infrastructure.Outbox;

namespace Norvia.DriverApp.Infrastructure.UnitTests;

/// <summary>The publisher sends committed outbox messages to the topic, oldest first, and marks each published only once Service Bus has it.</summary>
public sealed class OutboxPublisherTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
    private readonly ServiceBusClient _serviceBus = Substitute.For<ServiceBusClient>();
    private readonly ServiceBusSender _sender = Substitute.For<ServiceBusSender>();
    private readonly IRepository<OutboxMessage> _outbox = Substitute.For<IRepository<OutboxMessage>>();
    private readonly TaskCompletionSource _marked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ServiceBusMessage> _sent = [];

    private static OutboxMessage Message(int number) =>
        OutboxMessage.From(
            new TripStartReportedV1(Guid.NewGuid(), DateTimeOffset.UtcNow, $"corr-{number}", $"TR-000000{number}", 1, "DRV-0002"), DateTimeOffset.UtcNow);

    private OutboxPublisher Publisher(params OutboxMessage[] pending)
    {
        _serviceBus.CreateSender("driver-app-events").Returns(_sender);
        _sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _outbox.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<OutboxMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Ardalis.Specification.ISpecification<OutboxMessage>>().Evaluate(pending).ToList()));
        _outbox.UpdateAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (pending.All(message => message.PublishedAt is not null))
            {
                _marked.TrySetResult();
            }

            return Task.FromResult(0);
        });
        var services = new ServiceCollection().AddSingleton(_outbox).BuildServiceProvider();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.DriverApp.Api");
        return new OutboxPublisher(
            services.GetRequiredService<IServiceScopeFactory>(), _serviceBus, _time, environment, Options.Create(new OutboxOptions()), NullLogger<OutboxPublisher>.Instance);
    }

    [Fact]
    public async Task APendingMessage_IsSentToTheTopicInItsSession_ThenMarkedPublished()
    {
        var message = Message(1);
        var publisher = Publisher(message);

        await publisher.StartAsync(CancellationToken.None);
        await _marked.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await publisher.StopAsync(CancellationToken.None);

        var sent = _sent.Should().ContainSingle().Subject;
        sent.MessageId.Should().Be(message.EventId.ToString());
        sent.SessionId.Should().Be("TR-0000001");
        message.PublishedAt.Should().Be(_time.GetUtcNow());
        await _sender.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SeveralPendingMessages_AreSentOldestFirst_OnOneSender()
    {
        var first = Message(1);
        var second = Message(2);
        var publisher = Publisher(first, second);

        await publisher.StartAsync(CancellationToken.None);
        await _marked.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await publisher.StopAsync(CancellationToken.None);

        _sent.Select(sent => sent.MessageId).Should().Equal(first.EventId.ToString(), second.EventId.ToString());
        _serviceBus.Received(1).CreateSender("driver-app-events");
    }

    [Fact]
    public async Task WhenSendingFails_TheMessageIsNotMarked_AndTheNextCycleSendsItAgain()
    {
        var message = Message(1);
        var publisher = Publisher(message);
        var attempts = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                failed.SetResult();
                throw new ServiceBusException("Service Bus is down.", ServiceBusFailureReason.ServiceCommunicationProblem);
            }

            _sent.Add(null!);
            return Task.CompletedTask;
        });

        await publisher.StartAsync(CancellationToken.None);
        await failed.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        message.PublishedAt.Should().BeNull();
        _time.Advance(TimeSpan.FromSeconds(1));
        await _marked.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await publisher.StopAsync(CancellationToken.None);

        attempts.Should().Be(2);
        message.PublishedAt.Should().NotBeNull();
    }
}
