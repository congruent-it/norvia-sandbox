using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>Where the TMS API is, and how the TMS UI gets its token for it: signed locally, or from Entra in Azure.</summary>
public sealed class TmsApiOptions
{
    public const string SectionName = "TmsApi";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Set locally, where there is no Entra.</summary>
    public LocalTmsApiTokenOptions? LocalTokens { get; set; }

    /// <summary>Set in Azure.</summary>
    public EntraTmsApiTokenOptions? Entra { get; set; }
}
