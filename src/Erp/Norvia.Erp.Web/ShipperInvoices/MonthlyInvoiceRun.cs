using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// The monthly Invoice run (E1): for every Shipper, the same issuing path as Invoice now, over its Waiting lines delivered before the 1st
/// of the run's month (Amsterdam). Each Shipper runs in a scope and a transaction of its own, so one that fails leaves the others invoiced,
/// and a run that is repeated, or overlaps Invoice now, finds nothing Waiting for what is invoiced already.
/// </summary>
internal sealed partial class MonthlyInvoiceRun(IServiceScopeFactory scopes, ErpActivities activities, ILogger<MonthlyInvoiceRun> logger)
{
    /// <summary>Runs the month starting on <paramref name="firstOfMonth"/>. Returns false when a Shipper failed, so the caller runs again.</summary>
    public async Task<bool> RunAsync(DateOnly firstOfMonth, CancellationToken cancellationToken = default)
    {
        using var span = activities.Source.StartActivity("Monthly Invoice run", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.Invoicing });
        var cutoff = MonthlyInvoiceRunSchedule.CutoffOf(firstOfMonth);

        IReadOnlyList<ShipperView> shippers;
        try
        {
            await using var listing = scopes.CreateAsyncScope();
            shippers = await listing.ServiceProvider.GetRequiredService<IMediator>().Send(new ListShippersQuery(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            span?.SetTag(TrackingKeys.Outcome, TrackingKeys.Retrying);
            LogListingFailed(logger, exception);
            return false;
        }

        var allSucceeded = true;
        var issued = 0;
        foreach (var shipper in shippers)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var retrier = scope.ServiceProvider.GetRequiredService<IConflictRetrier>();
                var result = await retrier.RunAsync(mediator =>
                    mediator.Send(new IssueShipperInvoiceCommand(shipper.Id, cutoff), cancellationToken).AsTask());
                if (!result.IsSuccess)
                {
                    allSucceeded = false;
                    LogRefused(logger, shipper.Id, string.Join(' ', result.Errors));
                }
                else if (result.Value.Invoice is not null)
                {
                    issued++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                allSucceeded = false;
                LogFailed(logger, exception, shipper.Id);
            }
        }

        span?.SetTag(TrackingKeys.Outcome, allSucceeded ? TrackingKeys.Succeeded : TrackingKeys.Retrying);
        using var outcome = logger.Track(new() { [TrackingKeys.Outcome] = allSucceeded ? TrackingKeys.Succeeded : TrackingKeys.Retrying });
        LogRan(logger, firstOfMonth, shippers.Count, issued);
        return allSucceeded;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Monthly Invoice run for {Month:yyyy-MM} ran over {ShipperCount} Shippers and issued {InvoiceCount} invoices")]
    private static partial void LogRan(ILogger logger, DateOnly month, int shipperCount, int invoiceCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Monthly Invoice run couldn't list the Shippers; the next look runs it again")]
    private static partial void LogListingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Monthly Invoice run for {ShipperId} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string shipperId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Monthly Invoice run for {ShipperId} failed; the next look runs it again")]
    private static partial void LogFailed(ILogger logger, Exception exception, string shipperId);
}
