using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.PartnerPortal.Infrastructure.Faults;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Faults;

public sealed class LocalFaultTokensTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly FaultOwnerOptions DriverApp = new() { Address = new Uri("https://driver-app.example/"), Audience = "api://norvia-driver-app" };

    private static LocalFaultTokens Tokens(LocalFaultTokenOptions? local) =>
        new(Options.Create(new FaultsOptions { LocalTokens = local }), new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task GetTokenAsync_SignsAToken_ForTheOwnersAudience_HoldingTheDemoFaultRole()
    {
        var tokens = Tokens(new LocalFaultTokenOptions { Issuer = "https://norvia.local/tokens", ClientId = "norvia-local-partner-portal", SigningKey = Convert.ToBase64String(Key) });

        var token = await tokens.GetTokenAsync(DriverApp, TestContext.Current.CancellationToken);

        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://norvia.local/tokens",
            ValidAudience = "api://norvia-driver-app",
            IssuerSigningKey = new SymmetricSecurityKey(Key),
            ValidateLifetime = false,
        });
        validated.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)validated.SecurityToken;
        jwt.GetPayloadValue<string>("azp").Should().Be("norvia-local-partner-portal");
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("Demo.Fault");
    }

    [Fact]
    public async Task GetTokenAsync_WithoutLocalSettings_Throws()
    {
        var get = () => Tokens(local: null).GetTokenAsync(DriverApp, TestContext.Current.CancellationToken);

        await get.Should().ThrowAsync<InvalidOperationException>();
    }
}
