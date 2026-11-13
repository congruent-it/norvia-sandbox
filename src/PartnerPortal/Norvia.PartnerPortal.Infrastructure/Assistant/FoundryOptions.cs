namespace Norvia.PartnerPortal.Infrastructure.Assistant;

/// <summary>The Foundry project the Shipper tracking assistant is published in (ADR 0049), called as the portal's own managed identity.</summary>
public sealed class FoundryOptions
{
    /// <summary>The project's endpoint, such as <c>https://&lt;account&gt;.services.ai.azure.com/api/projects/norvia-intake</c>.</summary>
    public Uri? ProjectEndpoint { get; set; }

    /// <summary>The scope of the token the project takes.</summary>
    public string Scope { get; set; } = "https://ai.azure.com/.default";
}
