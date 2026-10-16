using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Reset;

/// <summary>Whether the TMS takes a reset (the Simulator's Reset estate), and which topic subscriptions must be empty first. Off unless the estate switches it on.</summary>
public sealed class ResetOptions
{
    public const string SectionName = "Reset";

    /// <summary>Off by default; on locally, and in Azure only where the estate switches it on.</summary>
    public bool Enabled { get; init; }

    /// <summary>The subscriptions that must hold no unprocessed message, each as <c>topic/subscription</c>.</summary>
    [Required]
    public string[] QuietSubscriptions { get; init; } = [];
}
