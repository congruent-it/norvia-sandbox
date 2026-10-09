using System.Globalization;

namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>
/// The warning P3 shows when a Shipment is assigned to Own fleet (P10): the collection day is Over capacity with the Shipment
/// counted, or it lies beyond the Planning horizon. It never blocks the assignment.
/// </summary>
/// <param name="Date">The Shipment's collection day.</param>
/// <param name="OutsideHorizon">True when the day is beyond the Planning horizon, so there is no load check for it.</param>
/// <param name="Load">The day's load check including the Shipment; null when outside the horizon.</param>
public sealed record LoadCheckWarning(DateOnly Date, bool OutsideHorizon, DayLoadView? Load)
{
    /// <summary>The warning as the Planner reads it, such as "Fri 26 Sep: Over capacity, 34 of 30 pallet places after this assignment".</summary>
    public string Text
    {
        get
        {
            var day = Date.ToString("ddd d MMM", CultureInfo.InvariantCulture);
            if (OutsideHorizon || Load is null)
            {
                return $"{day}: Outside the Planning horizon";
            }

            List<string> over = [];
            if (Load.DemandKg > Load.CapacityKg)
            {
                over.Add(string.Create(CultureInfo.InvariantCulture, $"{Load.DemandKg:0.##} of {Load.CapacityKg:0.##} kg"));
            }

            if (Load.DemandPalletPlaces > Load.CapacityPalletPlaces)
            {
                over.Add($"{Load.DemandPalletPlaces} of {Load.CapacityPalletPlaces} pallet places");
            }

            return $"{day}: Over capacity, {string.Join(" and ", over)} after this assignment";
        }
    }
}
