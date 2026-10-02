using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Shipments;

/// <summary>
/// Withdraws a Carrier booking with a call to the TMS API of its own, so each succeeds or is refused on its own.
/// </summary>
public sealed partial class CarrierBookingWithdrawals(ITransportOrderCommands commands, ILogger logger)
{
    /// <summary>
    /// The TMS API's answer: null when it couldn't be reached, so the Planner tries again, or when cancelled, as when the
    /// page closes.
    /// </summary>
    public async Task<WithdrawAnswer?> WithdrawAsync(string carrierBookingNumber, CancellationToken cancellationToken)
    {
        try
        {
            return await commands.WithdrawAsync(carrierBookingNumber, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogWithdrawFailed(exception, carrierBookingNumber);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Withdrawing Carrier booking {CarrierBookingNumber} failed")]
    private partial void LogWithdrawFailed(Exception exception, string carrierBookingNumber);
}
