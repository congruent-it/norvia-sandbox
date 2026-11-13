namespace Norvia.HealthCheck.Application.Cost;

/// <summary>The port to how many Transport Orders the TMS received each day, which the TMS API answers and the estate's bill is read against.</summary>
public interface ITransportOrderVolume
{
    /// <summary>The Transport Orders received per day, for each day of the window (both ends included) that had any.</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> CountPerDayAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);
}
