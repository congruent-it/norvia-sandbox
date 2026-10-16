using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Infrastructure.Entra;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Where the Norvia Partner API is and how each Persona calls it. Locally the Simulator signs the Persona tokens itself
/// (ADR 0023); in Azure they come from Entra and the subscription keys from Key Vault (ADR 0042). Exactly one of
/// <see cref="Entra"/> and <see cref="LocalTokens"/> is configured.
/// </summary>
public sealed class PartnerApiOptions
{
    public const string SectionName = "PartnerApi";

    /// <summary>The Norvia Partner API's address, such as <c>http://localhost:5100/partner/v1</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// How long to wait for Norvia's answer. The intake Logic App retries the TMS before it answers, so this is long, and a
    /// booking that takes longer is logged as not received.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Each Persona's client ID, as the estate outputs them: <c>personaId=clientId</c> pairs separated by semicolons.</summary>
    [Required]
    public string PersonaClientIds { get; set; } = string.Empty;

    /// <summary>Each Persona's subscription key for the Norvia partners product, by Persona ID.</summary>
    public Dictionary<string, string> SubscriptionKeys { get; set; } = [];

    /// <summary>Locally: the Simulator signs the Persona tokens itself, as Entra would (ADR 0023).</summary>
    public LocalTokenOptions? LocalTokens { get; set; }

    /// <summary>In Azure: each Persona's token comes from Entra, for the Persona's client app, which trusts the Simulator's managed identity (ADR 0042).</summary>
    public EntraPersonaTokenOptions? Entra { get; set; }

    public string ClientIdOf(string personaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personaId);
        return PersonaClientIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == personaId)
            .Select(pair => pair[1])
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No client ID is configured for Persona '{personaId}'.");
    }

    public string SubscriptionKeyOf(string personaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personaId);
        return SubscriptionKeys.GetValueOrDefault(personaId)
            ?? throw new InvalidOperationException($"No subscription key is configured for Persona '{personaId}'.");
    }
}
