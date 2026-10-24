using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// Makes the Driver reports Logic App as enabled as the fault says: disabled while the fault is on, enabled otherwise. A fault's expiry is only
/// the clock, so <see cref="DriverReportsLogicAppExpiry"/> runs this until it is enabled again, and it runs at start, so a fault that was on when the
/// portal restarted (which switches every fault off) never leaves the Logic App off.
/// </summary>
internal sealed partial class DriverReportsLogicAppFault(IFaults faults, ILogicAppSwitch logicApp, ILogger<DriverReportsLogicAppFault> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var enabled = !faults.IsOn(PartnerPortalFaultNames.DriverReportsLogicAppDisabled);
        if (await logicApp.IsEnabledAsync(cancellationToken) == enabled)
        {
            return;
        }

        await logicApp.SetEnabledAsync(enabled, cancellationToken);
        LogSwitched(enabled);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver reports Logic App is now enabled: {Enabled}")]
    private partial void LogSwitched(bool enabled);
}

internal sealed partial class DriverReportsLogicAppExpiry(DriverReportsLogicAppFault fault, ILogger<DriverReportsLogicAppExpiry> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await fault.ReconcileAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next tick tries again: the Logic App must not stay off because Azure was unreachable once.
                LogReconcileFailed(exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't bring the Driver reports Logic App to the state of its fault; trying again")]
    private partial void LogReconcileFailed(Exception exception);
}
