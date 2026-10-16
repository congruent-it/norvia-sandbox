using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Simulator.Infrastructure.TmsApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.TmsApi;

public sealed class LocalTmsTokensTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 3)).ToArray());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

    private LocalTmsTokens Tokens(LocalTmsTokenOptions? local = null) => new(
        Options.Create(new TmsApiOptions
        {
            BaseAddress = new Uri("https://tms.example"),
            LocalTokens = local ?? new LocalTmsTokenOptions { Issuer = "https://issuer", Audience = "api://tms", ClientId = "simulator", SigningKey = Key },
        }),
        _time);

    [Fact]
    public async Task GetToken_IsSignedAsTheEstateWouldIssueItToTheSimulator_WithTheManageAndResetRoles()
    {
        var token = await Tokens().GetTokenAsync(CancellationToken.None);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer",
            ValidAudience = "api://tms",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Key)),
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)validation.SecurityToken;
        jwt.GetClaim("azp").Value.Should().Be("simulator");
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("TransportOrders.Manage", "Estate.Reset");
        jwt.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetToken_NoLocalTokensConfigured_Throws()
    {
        var tokens = new LocalTmsTokens(Options.Create(new TmsApiOptions { BaseAddress = new Uri("https://tms.example") }), _time);

        var act = () => tokens.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TmsApi:LocalTokens*");
    }
}
