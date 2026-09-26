namespace Norvia.IntegrationConsole.Infrastructure.Runs;

/// <summary>Where the estate's Logic Apps are: the resource group the estate Bicep deploys them into.</summary>
public sealed class WorkflowRunsOptions
{
    public const string SectionName = "Estate";

    /// <summary>
    /// The resource group's ID, <c>/subscriptions/{subscription}/resourceGroups/{name}</c>, which the estate Bicep
    /// outputs. Empty locally until a developer points the console at a deployed estate; a run then says it can't be
    /// opened.
    /// </summary>
    public string? ResourceGroupId { get; set; }
}
