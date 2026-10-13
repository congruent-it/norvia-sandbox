using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Web.Operators;

/// <summary>Locally, where there is no Easy Auth, the operator the AppHost signs in: the developer.</summary>
public sealed class LocalOperatorOptions
{
    public const string SectionName = "LocalOperator";

    [Required]
    public string Name { get; init; } = "";

    /// <summary>The app roles the local operator holds, as Entra would give them.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}
