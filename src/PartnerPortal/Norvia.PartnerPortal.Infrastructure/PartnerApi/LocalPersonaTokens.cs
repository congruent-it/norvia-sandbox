using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Locally there is no Entra, so the portal signs each Persona's token itself with the key the local Partner API
/// trusts, as Entra would issue it: the Persona's client ID in <c>azp</c>, and the app roles the estate grants it
/// (ADR 0023).
/// </summary>
internal sealed class LocalPersonaTokens(IOptions<PartnerApiOptions> options, TimeProvider time) : IPersonaTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Task<string> GetTokenAsync(PersonaId personaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        var local = options.Value.LocalTokens ?? throw new InvalidOperationException("PartnerApi:LocalTokens isn't configured.");

        var claims = new Dictionary<string, object> { ["azp"] = options.Value.ClientIdOf(personaId) };
        // Entra leaves the claim out of the token of a client that holds no role.
        if (local.Roles.GetValueOrDefault(personaId.Value) is { Length: > 0 } roles)
        {
            claims["roles"] = roles;
        }

        var now = time.GetUtcNow().UtcDateTime;
        return Task.FromResult(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = local.Issuer,
            Audience = local.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        }));
    }
}
