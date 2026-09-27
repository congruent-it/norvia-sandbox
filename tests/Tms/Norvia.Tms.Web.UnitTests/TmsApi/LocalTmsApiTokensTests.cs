using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Tms.Web.TmsApi;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

public sealed class LocalTmsApiTokensTests
{
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(64);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly LocalTmsApiTokens _tokens;

    public LocalTmsApiTokensTests()
    {
        _tokens = new LocalTmsApiTokens(
            Options.Create(new TmsApiOptions
            {
                BaseAddress = new Uri("http://tms-api"),
                LocalTokens = new LocalTmsApiTokenOptions
                {
                    Issuer = "https://norvia.local/tokens",
                    Audience = "api://norvia-tms",
                    ClientId = "norvia-local-tms-ui",
                    SigningKey = Convert.ToBase64String(SigningKey),
                },
            }),
            _time);
    }

    [Fact]
    public async Task GetTokenAsync_IsSignedForTheTmsApi_AsTheTmsUi()
    {
        var token = await ValidatedAsync();

        token.Issuer.Should().Be("https://norvia.local/tokens");
        token.GetClaim("aud").Value.Should().Be("api://norvia-tms");
        token.GetClaim("azp").Value.Should().Be("norvia-local-tms-ui");
        token.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetTokenAsync_HoldsTheManageRole_AsTheEstateGrantsTheTmsUi()
    {
        var token = await ValidatedAsync();

        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("TransportOrders.Manage");
    }

    private async Task<JsonWebToken> ValidatedAsync()
    {
        var token = await _tokens.GetTokenAsync(Xunit.TestContext.Current.CancellationToken);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://norvia.local/tokens",
            ValidAudience = "api://norvia-tms",
            IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
            LifetimeValidator = (_, _, _, _) => true,
        });
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        return (JsonWebToken)validation.SecurityToken;
    }
}
