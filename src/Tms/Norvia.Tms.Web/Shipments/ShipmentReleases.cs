using System.Runtime.CompilerServices;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Shipments;

/// <summary>
/// Releases the Shipments a Planner selected, one call to the TMS API per Shipment, one after the other, so each
/// succeeds or is refused on its own and a refusal doesn't stop the rest.
/// </summary>
public sealed partial class ShipmentReleases(ITransportOrderCommands commands, ILogger logger)
{
    /// <summary>
    /// Each Shipment's answer as it comes: null when the TMS API couldn't be reached, so the Planner tries it again.
    /// Stops once cancelled, as when the page closes.
    /// </summary>
    public async IAsyncEnumerable<(string ShipmentNumber, ReleaseAnswer? Answer)> ReleaseEachAsync(
        IReadOnlyList<string> shipmentNumbers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumbers);
        foreach (var number in shipmentNumbers)
        {
            var answer = await ReleaseAsync(number, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return (number, answer);
        }
    }

    private async Task<ReleaseAnswer?> ReleaseAsync(string number, CancellationToken cancellationToken)
    {
        try
        {
            return await commands.ReleaseAsync(number, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReleaseFailed(exception, number);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing Shipment {ShipmentNumber} failed")]
    private partial void LogReleaseFailed(Exception exception, string shipmentNumber);
}
