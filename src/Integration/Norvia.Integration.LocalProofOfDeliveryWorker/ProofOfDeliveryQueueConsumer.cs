using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>
/// The Carrier Proof of delivery Logic App's trigger, played locally (ADR 0032): picks up a blob reference from the queue
/// in peek-lock mode and runs the flow for it. It settles the message itself, as the workflow's steps do: completed once
/// recorded, dead-lettered with the failure code when it can't be, abandoned when the TMS couldn't be reached, so the queue
/// delivers it again up to its maximum delivery count.
/// </summary>
internal sealed partial class ProofOfDeliveryQueueConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<ProofOfDeliveryWorkerOptions> options,
    ILogger<ProofOfDeliveryQueueConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = serviceBus.CreateProcessor(
            options.Value.Queue, new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 2 });
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
        var flow = scope.ServiceProvider.GetRequiredService<CarrierProofOfDeliveryFlow>();
        var properties = args.Message.ApplicationProperties.ToDictionary(property => property.Key, property => property.Value);
        var run = await flow.RunAsync(args.Message.Body.ToString(), properties, args.CancellationToken);
        if (run.RunFailed)
        {
            LogRunFailed(run.Settlement);
        }

        switch (run.Settlement)
        {
            case ProofOfDeliverySettlement.Complete:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                break;
            case ProofOfDeliverySettlement.DeadLetter:
                await args.DeadLetterMessageAsync(args.Message, run.DeadLetterReason, run.DeadLetterDescription, args.CancellationToken);
                break;
            default:
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Proof of delivery run failed (the message is settled as {Settlement})")]
    private partial void LogRunFailed(ProofOfDeliverySettlement settlement);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Proof of delivery queue's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
