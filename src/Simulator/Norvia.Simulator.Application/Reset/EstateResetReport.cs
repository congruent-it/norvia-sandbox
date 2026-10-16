namespace Norvia.Simulator.Application.Reset;

/// <summary>
/// What a reset did: each application's result, and the Simulator's own deletions, which happen only once every application has reset. A reset
/// that is not <see cref="Complete"/> can be run again.
/// </summary>
public sealed record EstateResetReport(IReadOnlyList<AppResetResult> Apps, IReadOnlyList<DeletedKind> Simulator)
{
    public bool Complete => Apps.All(app => app.Succeeded);
}
