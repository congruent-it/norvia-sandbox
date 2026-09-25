using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>Where the TMS is, and the identity the intake Logic App calls it as.</summary>
public sealed class TmsOptions
{
    public const string SectionName = "Tms";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The intake Logic App's token for the TMS; <see cref="LocalTokenOptions.ClientId"/> is its client ID.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();
}
