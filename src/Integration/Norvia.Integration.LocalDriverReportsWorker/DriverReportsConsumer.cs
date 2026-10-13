using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>
/// The Driver reports Logic App's trigger, played locally: reads the Driver reports subscription, whose sessions are keyed by Trip number, so one Trip's
/// reports are handled one at a time and in the order the Driver app published them, and runs the flow for each. As the workflow does, the message is
/// completed when the TMS recorded the report and dead-lettered with the failure code and message when it refused it or couldn't be reached, so the
/// session moves on to the next report.
/// </summary>
internal sealed partial class DriverReportsConsumer(
    ServiceBusClient serviceBus,
    IServiceScopeFactory scopes,
    IOptions<DriverReportsWorkerOptions> options,
    ILogger<DriverReportsConsumer> logger) : BackgroundService
{
    /// <summary>One call at a time in a session is what keeps a Trip's reports in order; different Trips are read side by side.</summary>
    internal static ServiceBusSessionProcessorOptions ProcessorOptions { get; } = new()
    {
        AutoCompleteMessages = false,
        MaxConcurrentSessions = 4,
        MaxConcurrentCallsPerSession = 1,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        await using var processor = serviceBus.CreateSessionProcessor(settings.Topic, settings.Subscription, ProcessorOptions);
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

    internal async Task HandleAsync(ProcessSessionMessageEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        await using var scope = scopes.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<DriverReportsFlow>();
        var outcome = await flow.RunAsync(
            args.Message.Body.ToString(),
            (settlement, cancellationToken) => settlement switch
            {
                Settlement.DeadLetter deadLetter => args.DeadLetterMessageAsync(args.Message, deadLetter.Reason, deadLetter.Description, cancellationToken),
                _ => args.CompleteMessageAsync(args.Message, cancellationToken),
            },
            args.CancellationToken);

        if (outcome == DriverReportsRunOutcome.OutcomeNotReported)
        {
            // The workflow's run fails and the report stays Sending in the Driver app; the TMS has it anyway. The message is settled, so it isn't redelivered.
            LogRunFailed(args.Message.SessionId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Driver reports run for Trip {TripNumber} couldn't tell the Driver app the outcome; the report stays Sending")]
    private partial void LogRunFailed(string tripNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Driver reports subscription's processor failed in {ErrorSource}")]
    private partial void LogProcessorFailed(Exception exception, ServiceBusErrorSource errorSource);
}
