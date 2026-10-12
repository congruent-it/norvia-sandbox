namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>How the run settles the message it read: completed, or dead-lettered with the failure code and the readable message (coding guidelines §7).</summary>
internal abstract record Settlement
{
    public static Settlement Complete { get; } = new Completed();

    private sealed record Completed : Settlement;

    /// <param name="Reason">The failure code, which becomes <c>DeadLetterReason</c>.</param>
    /// <param name="Description">The readable message, which becomes <c>DeadLetterErrorDescription</c>.</param>
    public sealed record DeadLetter(string Reason, string Description) : Settlement;
}
