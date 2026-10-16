namespace Norvia.PartnerPortal.Infrastructure.Reset;

/// <summary>Whether the Partner portal takes a reset (the Simulator's Reset estate). Off unless the estate switches it on.</summary>
public sealed class ResetOptions
{
    public const string SectionName = "Reset";

    /// <summary>Off by default; on locally, and in Azure only where the estate switches it on.</summary>
    public bool Enabled { get; init; }
}
