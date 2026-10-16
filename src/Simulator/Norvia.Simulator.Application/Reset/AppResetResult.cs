namespace Norvia.Simulator.Application.Reset;

/// <summary>What one application did when told to reset: what it deleted, or why it refused.</summary>
public sealed record AppResetResult(EstateApp App, IReadOnlyList<DeletedKind> Deleted, string? Refusal)
{
    public bool Succeeded => Refusal is null;

    public static AppResetResult Done(EstateApp app, IReadOnlyList<DeletedKind> deleted) => new(app, deleted, Refusal: null);

    public static AppResetResult Refused(EstateApp app, string refusal) => new(app, [], refusal);
}
