using System.Runtime.CompilerServices;
using Norvia.Agents;
using Norvia.Tms.Web.Agents;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Assigns the Transport Orders a Planner selected, one call to the TMS API per Transport Order, one after the other, so
/// each succeeds or is refused on its own and a refusal doesn't stop the rest.
/// </summary>
public sealed partial class TransportOrderAssignments(ITransportOrderCommands commands, ILogger logger)
{
    /// <summary>
    /// Each Transport Order's answer as it comes: null when the TMS API couldn't be reached, so the Planner tries it
    /// again. Stops once cancelled, as when the page closes.
    /// </summary>
    /// <param name="carrierId">The Carrier to carry them; null for Own fleet.</param>
    /// <param name="suggested">The Suggestion the Planner pre-selected, if any: the one order it is about names its Agent run when assigned as suggested.</param>
    public async IAsyncEnumerable<(string TransportOrderNumber, AssignAnswer? Answer)> AssignEachAsync(
        IReadOnlyList<string> transportOrderNumbers,
        string? carrierId,
        IEnumerable<SuggestedAssignment> suggested,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportOrderNumbers);
        ArgumentNullException.ThrowIfNull(suggested);
        foreach (var number in transportOrderNumbers)
        {
            var answer = await AssignAsync(number, carrierId, suggested.Select(one => one.RunFor(number, carrierId)).FirstOrDefault(run => run is not null), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return (number, answer);
        }
    }

    private async Task<AssignAnswer?> AssignAsync(string number, string? carrierId, AgentRun? actingOn, CancellationToken cancellationToken)
    {
        try
        {
            return await commands.AssignAsync(number, carrierId, actingOn, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAssignFailed(exception, number);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assigning Transport Order {TransportOrderNumber} failed")]
    private partial void LogAssignFailed(Exception exception, string transportOrderNumber);
}
