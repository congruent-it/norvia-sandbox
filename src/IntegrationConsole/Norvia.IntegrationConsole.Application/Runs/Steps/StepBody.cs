namespace Norvia.IntegrationConsole.Application.Runs.Steps;

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
