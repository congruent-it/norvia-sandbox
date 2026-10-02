using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalBookingWorker;

/// <summary>
/// The booking Logic App's trigger, played locally (ADR 0030): picks up <c>ShipmentReleased</c> from the booking
/// subscription and runs the flow for it. As with the Logic App's trigger, the message is completed once the run has
/// started: a run that fails is on the trail, not redelivered.
/// </summary>
internal sealed partial class ShipmentReleasedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<BookingWorkerOptions> options,
    ILogger<ShipmentReleasedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        await using var processor = serviceBus.CreateProcessor(
            settings.Topic, settings.Subscription, new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 4 });
        processor.ProcessMessageAsync += HandleAsync;
        processor.ProcessErrorAsync += args =>
        {
            LogProcessorFailed(args.Exception, args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping: let the messages in flight finish.
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }

    internal async Task HandleAsync(ProcessMessageEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        await using var scope = scopes.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<CarrierBookingFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != BookingFlowOutcome.Done)
        {
            // The workflow's run fails and the booking stays Requested until it is overdue (P4); the run is on the trail.
            LogRunFailed(outcome);
        }

        // The Logic App's Service Bus trigger completes a message once its run has started, so a failed run isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The booking run failed ({Outcome}); the booking stays Requested until it is overdue")]
    private partial void LogRunFailed(BookingFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The booking subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
