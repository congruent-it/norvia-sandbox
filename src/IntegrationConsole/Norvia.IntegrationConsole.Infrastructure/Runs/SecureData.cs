namespace Norvia.IntegrationConsole.Infrastructure.Runs;

/// <summary>Which of a step's bodies its definition keeps out of run history, with secure inputs or outputs.</summary>
internal sealed record SecureData(bool Inputs, bool Outputs)
{
    public static SecureData None { get; } = new(false, false);
}
