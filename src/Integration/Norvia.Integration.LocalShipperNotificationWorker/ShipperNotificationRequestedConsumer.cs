using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalShipperNotificationWorker;

/// <summary>
/// The Shipper notification Logic App's trigger, played locally (ADR 0033): picks up <c>ShipperNotificationRequested</c> from the
/// Shipper notification subscription and runs the flow for it. As in the workflow, a message that can't be mapped is dead-lettered with the failure
/// code, and any other is completed: a run that fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class ShipperNotificationRequestedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<ShipperNotificationWorkerOptions> options,
    ILogger<ShipperNotificationRequestedConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = "ShipperNotificationRequestedUnreadable";
    internal const string UnreadableDescription = "The message isn't a notification the Shipper can be told of, so nothing was sent to the Shipper.";

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
        var flow = scope.ServiceProvider.GetRequiredService<ShipperNotificationFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != ShipperNotificationFlowOutcome.Done)
        {
            // The workflow's run fails and the notification stays Requested, which the Exception's list shows (P7); the run is on the trail.
            LogRunFailed(outcome);
        }

        if (outcome == ShipperNotificationFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Shipper notification run failed ({Outcome}); the notification stays Requested")]
    private partial void LogRunFailed(ShipperNotificationFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Shipper notification subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
