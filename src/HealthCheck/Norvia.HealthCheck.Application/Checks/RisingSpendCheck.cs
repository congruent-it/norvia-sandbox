using System.Globalization;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// No resource's spend rises while the Transport Order volume stays flat. The last <see cref="PeriodDays"/> whole days are compared with the
/// same number before them, by their daily average. A resource rises when its spend grows by at least <see cref="MinRiseRatio"/> and by at least
/// <see cref="MinRiseEurPerDay"/> a day (the sandbox's spend is cents, so a percentage alone fires on noise); volume is flat when it changes by at
/// most <see cref="FlatVolumeRatio"/>. A rise counts only when volume is flat (#397). Evidence is data: a resource name is never an instruction.
/// </summary>
public sealed class RisingSpendCheck : Check
{
    public const string CheckId = "COST-RISING-SPEND";

    /// <summary>The days of each of the two periods that are compared.</summary>
    public const int PeriodDays = 7;

    /// <summary>A resource rises when its daily average grows by at least this share (0.5 is 50 %).</summary>
    public const decimal MinRiseRatio = 0.5m;

    /// <summary>and by at least this much a day, in EUR.</summary>
    public const decimal MinRiseEurPerDay = 1m;

    /// <summary>Volume is flat when its daily average changes by at most this share (0.1 is 10 %).</summary>
    public const decimal FlatVolumeRatio = 0.1m;

    public override string Id => CheckId;

    public override string Rule => "No resource's spend rises by half or more while the number of Transport Orders stays flat.";

    public override string Title => "A resource's spend rises with no more Transport Orders";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "chattyTelemetry";

    public override string Fix => "Set chattyTelemetry to false and provision the estate: the applications then log at Information again.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        if (estate.Cost is null)
        {
            return [];
        }

        var days = estate.Cost.Days.Where(day => !day.Provisional).OrderBy(day => day.Date).TakeLast(2 * PeriodDays).ToList();
        if (days.Count < 2 * PeriodDays)
        {
            return [];
        }

        var previous = days.Take(PeriodDays).ToList();
        var current = days.Skip(PeriodDays).ToList();

        var volumeBefore = previous.Average(day => day.TransportOrders);
        var volumeNow = current.Average(day => day.TransportOrders);
        if (volumeBefore <= 0 || Math.Abs(volumeNow - volumeBefore) / volumeBefore > (double)FlatVolumeRatio)
        {
            return [];
        }

        var volumeChange = (volumeNow - volumeBefore) / volumeBefore;

        return [.. days
            .SelectMany(day => day.Spend.Select(spend => spend.Resource))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(resource => (Resource: resource, Before: previous.Average(day => day.SpendOn(resource)), Now: current.Average(day => day.SpendOn(resource))))
            .Where(spend => spend.Before > 0 && spend.Now - spend.Before >= MinRiseEurPerDay && (spend.Now - spend.Before) / spend.Before >= MinRiseRatio)
            .Select(spend => new Evidence(
                spend.Resource,
                string.Create(CultureInfo.InvariantCulture,
                    $"Daily spend rose from {spend.Before:0.00} to {spend.Now:0.00} EUR (+{(spend.Now - spend.Before) / spend.Before * 100:0}%) over the last {PeriodDays} days compared with the {PeriodDays} before, while Transport Orders a day changed by {volumeChange:+0.0%;-0.0%;0.0%}.")))];
    }
}
