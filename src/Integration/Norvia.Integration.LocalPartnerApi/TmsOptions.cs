using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>Where the TMS is, and the identities the intake and Carrier-answer Logic Apps call it as.</summary>
public sealed class TmsOptions
{
    public const string SectionName = "Tms";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The tokens for the TMS: <see cref="LocalTokenOptions.ClientId"/> is the intake Logic App's client ID.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();

    /// <summary>The Carrier-answer Logic App's client ID, the one holding the TMS answer role.</summary>
    [Required]
    public string? AnswerClientId { get; set; }

    /// <summary>API Management's client ID, the one holding the TMS Shipper tracking role.</summary>
    [Required]
    public string? ShipperTrackingClientId { get; set; }
}
