namespace Norvia.IntegrationConsole.Application.Runs.Steps;

/// <summary>How one of a step's bodies shows.</summary>
public enum BodyState
{
    /// <summary>In full.</summary>
    Shown,

    /// <summary>The step uses secure inputs or outputs: the body exists, but isn't shown.</summary>
    Hidden,

    /// <summary>Run history has no body.</summary>
    None,
}

/// <summary>One of a step's bodies on the run detail page.</summary>
/// <param name="Content">The body in full when it's <see cref="BodyState.Shown"/>, otherwise null.</param>
public sealed record StepBody(BodyState State, string? Content)
{
    public static StepBody Hidden { get; } = new(BodyState.Hidden, null);

    public static StepBody None { get; } = new(BodyState.None, null);

    public static StepBody Shown(string content) => new(BodyState.Shown, content);

    internal static StepBody Of(RecordedBody body) => body switch
    {
        { Secure: true } => Hidden,
        { Content: { Length: > 0 } content } => Shown(content),
        _ => None,
    };
}
