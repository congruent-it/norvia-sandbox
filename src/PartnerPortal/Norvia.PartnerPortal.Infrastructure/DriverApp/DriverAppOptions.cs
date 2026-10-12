using System.ComponentModel.DataAnnotations;

namespace Norvia.PartnerPortal.Infrastructure.DriverApp;

/// <summary>Where the Driver app's API is, for the presenter panel's "Driver app unavailable" fault.</summary>
public sealed class DriverAppOptions
{
    public const string SectionName = "DriverApp";

    [Required]
    public Uri? Address { get; set; }
}
