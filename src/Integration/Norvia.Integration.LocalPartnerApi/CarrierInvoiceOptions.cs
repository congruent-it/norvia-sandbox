using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>Where the Carrier invoice Logic App is, and the identity API Management calls it as.</summary>
public sealed class CarrierInvoiceOptions
{
    public const string SectionName = "CarrierInvoice";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The tokens for the Carrier invoice Logic App's trigger: <see cref="LocalTokenOptions.ClientId"/> is API Management's client ID.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();
}
