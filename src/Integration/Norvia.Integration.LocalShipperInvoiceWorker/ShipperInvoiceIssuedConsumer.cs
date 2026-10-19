using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalShipperInvoiceWorker;

/// <summary>
/// The Shipper invoice Logic App's trigger, played locally (ADR 0033, ADR 0043): picks up <c>ShipperInvoiceIssued</c> from the Shipper
/// invoice subscription and runs the flow for it. As in the workflow, a message that can't be mapped is dead-lettered with the failure
/// code, and any other is completed: a run that fails later is on the trail, not redelivered.
/// </summary>
internal sealed partial class ShipperInvoiceIssuedConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<ShipperInvoiceWorkerOptions> options,
    ILogger<ShipperInvoiceIssuedConsumer> logger) : BackgroundService
{
    internal const string UnreadableCode = "ShipperInvoiceIssuedUnreadable";
    internal const string UnreadableDescription = "The message isn't an invoice the Shipper can be sent, so nothing was sent to the Shipper.";

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
        var flow = scope.ServiceProvider.GetRequiredService<ShipperInvoiceFlow>();
        var outcome = await flow.RunAsync(args.Message.Body.ToString(), args.CancellationToken);
        if (outcome != ShipperInvoiceFlowOutcome.Done)
        {
            // The workflow's run fails and the invoice stays Issued without a sent time, which the ERP's list shows; the run is on the trail.
            LogRunFailed(outcome);
        }

        if (outcome == ShipperInvoiceFlowOutcome.Unreadable)
        {
            // As the workflow does: the failure code and a readable message, for the Integration operator to find (coding guidelines §7).
            await args.DeadLetterMessageAsync(args.Message, UnreadableCode, UnreadableDescription, args.CancellationToken);
            return;
        }

        // The workflow completes a message once it has mapped it, so a run that fails later isn't redelivered.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Shipper invoice run failed ({Outcome}); the invoice stays Issued without a sent time")]
    private partial void LogRunFailed(ShipperInvoiceFlowOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Shipper invoice subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
