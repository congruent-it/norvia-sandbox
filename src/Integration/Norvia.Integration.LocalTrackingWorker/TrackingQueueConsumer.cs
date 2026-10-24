using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalTrackingWorker;

/// <summary>
/// The Carrier-tracking Logic App's trigger, played locally (ADR 0031): picks up a tracking message from the queue in
/// peek-lock mode and runs the flow for it. Unlike the booking trigger it settles the message itself, as the workflow's
/// steps do: completed once recorded, dead-lettered with the failure code when it can't be, abandoned when the TMS
/// couldn't be reached, so the queue delivers it again up to its maximum delivery count.
/// </summary>
internal sealed partial class TrackingQueueConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<TrackingWorkerOptions> options,
    TimeProvider time,
    ILogger<TrackingQueueConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = serviceBus.CreateProcessor(
            options.Value.Queue, new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 4 });
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
        var flow = scope.ServiceProvider.GetRequiredService<CarrierTrackingFlow>();
        var properties = args.Message.ApplicationProperties.ToDictionary(property => property.Key, property => property.Value);
        var run = await flow.RunAsync(args.Message.Body.ToString(), properties, args.CancellationToken, args.Message.MessageId);
        if (run.RunFailed)
        {
            LogRunFailed(run.Settlement);
        }

        switch (run.Settlement)
        {
            case TrackingSettlement.Complete:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                break;
            case TrackingSettlement.DeadLetter:
                await args.DeadLetterMessageAsync(args.Message, run.DeadLetterReason, run.DeadLetterDescription, args.CancellationToken);
                break;
            default:
                if (options.Value.RedeliveryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.Value.RedeliveryDelay, time, args.CancellationToken);
                }

                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The tracking run failed (the message is settled as {Settlement})")]
    private partial void LogRunFailed(TrackingSettlement settlement);

    [LoggerMessage(Level = LogLevel.Error, Message = "The tracking queue's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
