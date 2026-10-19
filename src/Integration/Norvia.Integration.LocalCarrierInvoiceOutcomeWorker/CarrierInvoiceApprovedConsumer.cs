using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalCarrierInvoiceOutcomeWorker;

/// <summary>
/// The Carrier invoice outcome Logic App's trigger, played locally (ADR 0033, ADR 0043): picks up <c>CarrierInvoiceApproved</c> from the Carrier invoice outcome subscription and runs the flow for it. As in the workflow, a message that can't be mapped is dead-lettered with the failure
/// code, and any other is completed: a run that fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class CarrierInvoiceApprovedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<CarrierInvoiceOutcomeWorkerOptions> options,
    ILogger<CarrierInvoiceApprovedConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = "CarrierInvoiceApprovedUnreadable";
    internal const string UnreadableDescription = "The message isn't an approved Carrier invoice the Carrier can be told, so nothing was sent to the Carrier.";

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
        var flow = scope.ServiceProvider.GetRequiredService<CarrierInvoiceOutcomeFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != CarrierInvoiceOutcomeFlowOutcome.Done)
        {
            // The workflow's run fails and the invoice shows neither a told time nor the Carrier not told flag; the run is on the trail.
            LogRunFailed(outcome);
        }

        if (outcome == CarrierInvoiceOutcomeFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Carrier invoice outcome run failed ({Outcome}); the invoice shows neither a told time nor the flag")]
    private partial void LogRunFailed(CarrierInvoiceOutcomeFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Carrier invoice outcome subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
