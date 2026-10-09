namespace Norvia.Tms.Domain.Fleet;

/// <summary>The limits of the Driving-hour budget (P10), a simplified EU Regulation 561/2006, from the TMS settings.</summary>
/// <param name="Daily">The planned driving a day normally allows.</param>
/// <param name="ExtendedDaily">What a day may be extended to, using one of the week's extensions.</param>
/// <param name="ExtensionsPerWeek">How many days a week may be extended.</param>
/// <param name="Weekly">The limit for a fixed week, Monday to Sunday.</param>
/// <param name="Fortnightly">The limit for this week and the previous one.</param>
public sealed record DrivingHourLimits(TimeSpan Daily, TimeSpan ExtendedDaily, int ExtensionsPerWeek, TimeSpan Weekly, TimeSpan Fortnightly)
{
    /// <summary>9 h a day, 10 h at most twice a week, 56 h a week and 90 h a fortnight.</summary>
    public static readonly DrivingHourLimits Default =
        new(TimeSpan.FromHours(9), TimeSpan.FromHours(10), 2, TimeSpan.FromHours(56), TimeSpan.FromHours(90));
}
