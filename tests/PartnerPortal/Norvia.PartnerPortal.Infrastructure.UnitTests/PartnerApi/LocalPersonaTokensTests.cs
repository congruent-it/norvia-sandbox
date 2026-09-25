using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class LocalPersonaTokensTests
{
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(64);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly LocalPersonaTokens _tokens;

    public LocalPersonaTokensTests()
    {
        _tokens = new LocalPersonaTokens(
            Options.Create(new PartnerApiOptions
            {
                BaseAddress = new Uri("http://partner-api/partner/v1/"),
                PersonaClientIds = "brouwer-dranken=norvia-local-persona-brouwer-dranken;van-dijk-transport=norvia-local-persona-van-dijk-transport",
                LocalTokens = new LocalTokenOptions
                {
                    Issuer = "https://norvia.local/tokens",
                    Audience = "api://norvia-partner-api",
                    SigningKey = Convert.ToBase64String(SigningKey),
                    Roles = new() { ["brouwer-dranken"] = ["TransportOrders.Submit"] },
                },
            }),
            _time);
    }

    [Fact]
    public async Task GetTokenAsync_IsSignedForTheNorviaPartnerApi_AsThePersonasClient()
    {
        var token = await ValidatedAsync("brouwer-dranken");

        token.Issuer.Should().Be("https://norvia.local/tokens");
        token.GetClaim("aud").Value.Should().Be("api://norvia-partner-api");
        token.GetClaim("azp").Value.Should().Be("norvia-local-persona-brouwer-dranken");
        token.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetTokenAsync_ShipperPersona_HoldsItsRoles()
    {
        var token = await ValidatedAsync("brouwer-dranken");

        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("TransportOrders.Submit");
    }

    [Fact]
    public async Task GetTokenAsync_PersonaWithoutRoles_HasNoRolesClaim_AsEntraIssuesIt()
    {
        var token = await ValidatedAsync("van-dijk-transport");

        token.Claims.Should().NotContain(claim => claim.Type == "roles");
    }

    private async Task<JsonWebToken> ValidatedAsync(string personaId)
    {
        var token = await _tokens.GetTokenAsync(new PersonaId(personaId), TestContext.Current.CancellationToken);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://norvia.local/tokens",
            ValidAudience = "api://norvia-partner-api",
            IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        return (JsonWebToken)validation.SecurityToken;
    }
}
