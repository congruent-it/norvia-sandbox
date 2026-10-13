using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class LocalPersonaTokensTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    private LocalPersonaTokens Tokens(Dictionary<string, string[]>? roles = null) => new(
        Options.Create(new PartnerApiOptions
        {
            PersonaClientIds = "brouwer-dranken=client-1",
            LocalTokens = new LocalTokenOptions { Issuer = "https://issuer", Audience = "api://partner", SigningKey = Key, Roles = roles ?? new() { ["brouwer-dranken"] = ["TransportOrders.Submit"] } },
        }),
        _time);

    [Fact]
    public async Task GetToken_SignsAsTheEstateWouldIssueItToThePersonasClient()
    {
        var token = await Tokens().GetTokenAsync("brouwer-dranken", CancellationToken.None);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer",
            ValidAudience = "api://partner",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Key)),
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)validation.SecurityToken;
        jwt.GetClaim("azp").Value.Should().Be("client-1");
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("TransportOrders.Submit");
        jwt.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetToken_APersonaWithoutRoles_HasNoRolesClaim()
    {
        var token = await Tokens(roles: []).GetTokenAsync("brouwer-dranken", CancellationToken.None);

        new JsonWebToken(token).TryGetPayloadValue<string[]>("roles", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetToken_APersonaWithoutAClientId_Throws()
    {
        var act = () => Tokens().GetTokenAsync("nobody", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetToken_WithoutLocalTokenSettings_Throws()
    {
        var tokens = new LocalPersonaTokens(Options.Create(new PartnerApiOptions()), _time);

        var act = () => tokens.GetTokenAsync("brouwer-dranken", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*LocalTokens*");
    }
}
