namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>How a dead-letter queue reads on the page.</summary>
public static class DeadLetterText
{
    /// <summary>"3 d 4 h", "2 h 5 min", "7 min" or "under a minute", or a dash when there is no message.</summary>
    public static string Age(TimeSpan? age) => age switch
    {
        null => "–",
        { TotalDays: >= 1 } days => $"{(int)days.TotalDays} d {days.Hours} h",
        { TotalHours: >= 1 } hours => $"{(int)hours.TotalHours} h {hours.Minutes} min",
        { TotalMinutes: >= 1 } minutes => $"{(int)minutes.TotalMinutes} min",
        _ => "under a minute",
    };
}
