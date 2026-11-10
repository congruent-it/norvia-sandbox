namespace Norvia.Integration.BookingDraft.Api;

/// <summary>The Foundry project the Intake agent is published in (<c>norvia-intake</c>, which has no Application Insights connection).</summary>
public sealed class FoundryOptions
{
    /// <summary>The project's endpoint, such as <c>https://&lt;account&gt;.services.ai.azure.com/api/projects/norvia-intake</c>.</summary>
    public Uri? ProjectEndpoint { get; set; }

    /// <summary>The scope of the token the project takes.</summary>
    public string Scope { get; set; } = "https://ai.azure.com/.default";
}
