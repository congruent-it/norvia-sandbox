using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>Where the Driver app is and the Driver reports Logic App's token for its outcome endpoint (<c>DriverReports.ReportOutcome</c>).</summary>
public sealed class DriverAppOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }

    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions Tokens { get; set; } = new();
}
