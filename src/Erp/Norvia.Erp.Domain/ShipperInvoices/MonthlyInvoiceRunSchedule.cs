namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>
/// When the monthly Invoice run happens: the 1st of each month at 06:00 Europe/Amsterdam, over the lines delivered before that 1st.
/// </summary>
public static class MonthlyInvoiceRunSchedule
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeSpan RunTime = TimeSpan.FromHours(6);

    /// <summary>The latest scheduled run that is not after <paramref name="now"/>, as the 1st of its month in Amsterdam.</summary>
    public static DateOnly LatestRunMonth(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Amsterdam).DateTime;
        var first = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return DateOnly.FromDateTime(local >= first + RunTime ? first : first.AddMonths(-1));
    }

    /// <summary>The cutoff of the run of a month: midnight at the start of its 1st, Amsterdam. Lines delivered before it are invoiced.</summary>
    public static DateTimeOffset CutoffOf(DateOnly firstOfMonth) => AmsterdamInstant(firstOfMonth.ToDateTime(TimeOnly.MinValue));

    /// <summary>The instant the run of a month is due.</summary>
    public static DateTimeOffset DueAt(DateOnly firstOfMonth) => AmsterdamInstant(firstOfMonth.ToDateTime(TimeOnly.MinValue) + RunTime);

    private static DateTimeOffset AmsterdamInstant(DateTime local) =>
        new(local, Amsterdam.GetUtcOffset(local));
}
