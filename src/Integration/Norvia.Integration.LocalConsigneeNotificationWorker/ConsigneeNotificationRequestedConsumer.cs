using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalConsigneeNotificationWorker;

/// <summary>
/// The Consignee notification Logic App's trigger, played locally (ADR 0033): picks up <c>ConsigneeNotificationRequested</c> from the
/// Consignee notification subscription and runs the flow for it. As in the workflow, a message that can't be mapped is dead-lettered with the failure
/// code, and any other is completed: a run that fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class ConsigneeNotificationRequestedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<ConsigneeNotificationWorkerOptions> options,
    ILogger<ConsigneeNotificationRequestedConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = "ConsigneeNotificationRequestedUnreadable";
    internal const string UnreadableDescription = "The message isn't a notification the Consignee can be told of, so nothing was sent to the Consignee.";

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
        var flow = scope.ServiceProvider.GetRequiredService<ConsigneeNotificationFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != ConsigneeNotificationFlowOutcome.Done)
        {
            // The workflow's run fails and is on the trail; the message was completed, so it is not redelivered.
            LogRunFailed(outcome);
        }

        if (outcome == ConsigneeNotificationFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Consignee notification run failed ({Outcome})")]
    private partial void LogRunFailed(ConsigneeNotificationFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Consignee notification subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
