using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>
/// The Trip dispatch Logic App's trigger, played locally: picks up <c>TripDispatched</c> from the Trip dispatch subscription and runs the flow
/// for it. As in the workflow, a message that isn't a Trip version is dead-lettered with the failure code, and any other is completed: a run that
/// fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class TripDispatchConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<TripDispatchWorkerOptions> options,
    ILogger<TripDispatchConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = TripDispatchFailureCodes.Unreadable;
    internal const string UnreadableDescription = "The message isn't a Trip version the Driver app can be sent, so nothing was sent to the Driver app.";

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
        var flow = scope.ServiceProvider.GetRequiredService<TripDispatchFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != TripDispatchFlowOutcome.Done)
        {
            // The workflow's run fails and the version stays Sending, which the Planner's Trip detail shows (P11); the run is on the trail.
            LogRunFailed(outcome);
        }

        if (outcome == TripDispatchFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Trip dispatch run failed ({Outcome}); the Trip version stays Sending")]
    private partial void LogRunFailed(TripDispatchFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Trip dispatch subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
