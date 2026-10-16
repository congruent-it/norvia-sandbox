using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>
/// Locally there is no Entra, so the Simulator signs a Driver's token itself with the key the Driver app trusts locally, as Entra would issue it to the
/// Driver's account: the Simulator's client ID in <c>azp</c>, and the Driver ID as the one app role.
/// </summary>
internal sealed class LocalDriverTokens(IOptions<DriverApiOptions> options, TimeProvider time) : IDriverTokens
{
    /// <summary>The Driver app API's app role for the reset door.</summary>
    public const string ResetRole = "Estate.Reset";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Task<string> GetTokenAsync(string driverId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        return Task.FromResult(Sign(driverId));
    }

    public Task<string> GetResetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Sign(ResetRole));

    private string Sign(string role)
    {
        var local = options.Value.LocalTokens ?? throw new InvalidOperationException("DriverApp:LocalTokens isn't configured.");
        var now = time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = local.Issuer,
            Audience = local.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object> { ["azp"] = local.ClientId, ["roles"] = new[] { role } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
    }
}
