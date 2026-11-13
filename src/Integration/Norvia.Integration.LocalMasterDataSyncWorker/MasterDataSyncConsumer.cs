using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalMasterDataSyncWorker;

/// <summary>
/// The E3 Logic App's trigger, played locally: picks up a master-data event from the master-data sync subscription and runs the flow for
/// it. As in the workflow, the message is completed once the TMS replaced its copy, and dead-lettered with the failure code and a readable
/// message when it can't be read or the TMS refused it or didn't answer after the retries.
/// </summary>
internal sealed partial class MasterDataSyncConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<MasterDataSyncWorkerOptions> options,
    ILogger<MasterDataSyncConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        // The lock outlasts the retries of one call, as the subscription's does in Azure.
        await using var processor = serviceBus.CreateProcessor(
            settings.Topic,
            settings.Subscription,
            new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 4, MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5) });
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
        var run = await scope.ServiceProvider.GetRequiredService<MasterDataSyncFlow>().RunAsync(args.Message.Body.ToString(), args.CancellationToken, args.Message.MessageId);
        if (run.Replaced)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // The workflow's run fails and the TMS keeps its old copy until the message is resubmitted.
        LogRunFailed(run.Reason!);
        await args.DeadLetterMessageAsync(args.Message, run.Reason!, run.Description, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The master-data sync run failed ({Reason}); the message is dead-lettered")]
    private partial void LogRunFailed(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "The master-data sync subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
