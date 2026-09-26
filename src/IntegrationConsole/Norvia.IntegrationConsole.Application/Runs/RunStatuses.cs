namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>The statuses Logic Apps gives a run or a step that ended without doing what it was for.</summary>
internal static class RunStatuses
{
    public static bool IsFailure(string? status) => status is "Failed" or "TimedOut" or "Aborted" or "Cancelled";
}
