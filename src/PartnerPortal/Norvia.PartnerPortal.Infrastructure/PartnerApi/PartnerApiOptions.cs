using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Where the Norvia Partner API is and how each Persona calls it: API Management's in Azure, the local stand-in under
/// Aspire (ADR 0023).
/// </summary>
public sealed class PartnerApiOptions
{
    public const string SectionName = "PartnerApi";

    /// <summary>The Norvia Partner API's address, such as <c>https://&lt;API Management&gt;.azure-api.net/partner/v1</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// How long to wait for Norvia's answer. The intake Logic App retries the TMS before it answers, so this is long,
    /// and a booking that takes longer is shown as not received.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Each Persona's client ID, as the estate outputs them: <c>personaId=clientId</c> pairs separated by semicolons.</summary>
    [Required]
    public string PersonaClientIds { get; set; } = string.Empty;

    /// <summary>Each Persona's subscription key for the Norvia partners product, by Persona ID.</summary>
    public Dictionary<string, string> SubscriptionKeys { get; set; } = [];

    /// <summary>In Azure: the Persona tokens come from Entra, through federated credentials that trust the portal's identity (ADR 0005).</summary>
    public EntraTokenOptions? Entra { get; set; }

    /// <summary>Locally: the portal signs the Persona tokens itself, as Entra would (ADR 0023).</summary>
    public LocalTokenOptions? LocalTokens { get; set; }

    public string ClientIdOf(PersonaId personaId)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        return PersonaClientIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == personaId.Value)
            .Select(pair => pair[1])
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No client ID is configured for Persona '{personaId}'.");
    }

    public string SubscriptionKeyOf(PersonaId personaId)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        return SubscriptionKeys.GetValueOrDefault(personaId.Value)
            ?? throw new InvalidOperationException($"No subscription key is configured for Persona '{personaId}'.");
    }
}
