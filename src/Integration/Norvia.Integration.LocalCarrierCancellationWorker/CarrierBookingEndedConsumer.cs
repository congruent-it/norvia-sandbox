using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>
/// The Carrier cancellation Logic App's trigger, played locally (ADR 0034): picks up <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c> from the
/// Carrier cancellation subscription and runs the flow for it. As in the workflow, a message that can't be mapped is dead-lettered with the failure
/// code, and any other is completed: a run that fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class CarrierBookingEndedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<CarrierCancellationWorkerOptions> options,
    ILogger<CarrierBookingEndedConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = "CarrierCancellationUnreadable";
    internal const string UnreadableDescription = "The message isn't a cancellation the Carrier can be told of, so nothing was sent to the Carrier.";

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
        var flow = scope.ServiceProvider.GetRequiredService<CarrierCancellationFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != CarrierCancellationFlowOutcome.Done)
        {
            // The workflow's run fails and the Carrier booking stays without a Carrier told time, which the Planner's list shows (P7); the run is on the trail.
            LogRunFailed(outcome);
        }

        if (outcome == CarrierCancellationFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Carrier cancellation run failed ({Outcome}); the Carrier booking stays without a Carrier told time")]
    private partial void LogRunFailed(CarrierCancellationFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Carrier cancellation subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
