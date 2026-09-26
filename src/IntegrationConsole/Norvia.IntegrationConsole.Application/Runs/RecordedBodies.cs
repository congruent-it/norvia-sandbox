namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>A step's input and output bodies as run history has them.</summary>
public sealed record RecordedBodies(RecordedBody Input, RecordedBody Output);

/// <summary>One of a step's bodies.</summary>
/// <param name="Secure">Whether the step keeps it behind secure inputs or outputs, so it isn't fetched.</param>
/// <param name="Content">The body in full, or null when there is none or it wasn't fetched.</param>
public sealed record RecordedBody(bool Secure, string? Content)
{
    public static RecordedBody None { get; } = new(false, null);

    public static RecordedBody Hidden { get; } = new(true, null);
}
