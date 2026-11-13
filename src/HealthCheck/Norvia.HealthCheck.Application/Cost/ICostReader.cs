namespace Norvia.HealthCheck.Application.Cost;

/// <summary>
/// The port to what the estate costs. Infrastructure implements it: locally a modelled snapshot, in Azure Cost Management as the app's
/// own managed identity, with the Transport Order counts from the TMS API. The numbers come from deterministic code, never a model.
/// </summary>
public interface ICostReader
{
    /// <summary>
    /// Spend per resource per day, in EUR, with the Transport Orders created that day, for the whole past days of the window
    /// <paramref name="firstDay"/> to <paramref name="lastDay"/> (both included). Azure leaves today out, as it is not a whole day; the local snapshot answers the days it holds.
    /// Cost Management settles a day's cost about a day late, so Azure marks yesterday <see cref="CostDay.Provisional"/>; the
    /// consumer shows that and never guesses the number.
    /// </summary>
    Task<IReadOnlyList<CostDay>> ReadAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);
}
