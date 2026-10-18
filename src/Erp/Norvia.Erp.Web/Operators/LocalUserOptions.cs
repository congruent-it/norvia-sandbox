using System.ComponentModel.DataAnnotations;

namespace Norvia.Erp.Web.Operators;

/// <summary>Locally, where there is no Easy Auth, the user the AppHost signs in: the developer.</summary>
public sealed class LocalUserOptions
{
    public const string SectionName = "LocalUser";

    [Required]
    public string Name { get; init; } = "";

    /// <summary>The app roles the local user holds, as Entra would give them.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}
