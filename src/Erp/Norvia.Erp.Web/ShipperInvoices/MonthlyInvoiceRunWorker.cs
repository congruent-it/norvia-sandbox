using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// Starts the <see cref="MonthlyInvoiceRun"/> on the 1st of each month at 06:00 Europe/Amsterdam. It looks every minute whether a run is
/// due, and when the ERP starts, so an ERP that was down at 06:00 catches up as soon as it is back; a run over nothing Waiting changes
/// nothing. The clock is the injected <see cref="TimeProvider"/>, so tests control it. A run in which a Shipper failed is tried again at the next look.
/// </summary>
internal sealed class MonthlyInvoiceRunWorker(MonthlyInvoiceRun run, TimeProvider time) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private DateOnly? _lastRunMonth;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        do
        {
            await RunIfDueAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        var month = MonthlyInvoiceRunSchedule.LatestRunMonth(time.GetUtcNow());
        if (month == _lastRunMonth)
        {
            return;
        }

        if (await run.RunAsync(month, cancellationToken))
        {
            _lastRunMonth = month;
        }
    }
}
