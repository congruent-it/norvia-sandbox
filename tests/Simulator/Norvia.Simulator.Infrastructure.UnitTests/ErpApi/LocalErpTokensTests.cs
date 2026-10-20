using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Simulator.Infrastructure.ErpApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.ErpApi;

public sealed class LocalErpTokensTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 5)).ToArray());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetToken_IsSignedAsTheEstateWouldIssueItToTheSimulator_WithTheManageRoles()
    {
        var tokens = new LocalErpTokens(
            Options.Create(new ErpApiOptions
            {
                BaseAddress = new Uri("https://erp.example"),
                LocalTokens = new LocalErpTokenOptions { Issuer = "https://issuer", Audience = "api://erp", ClientId = "simulator", SigningKey = Key },
            }),
            _time);

        var token = await tokens.GetTokenAsync(TestContext.Current.CancellationToken);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer",
            ValidAudience = "api://erp",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Key)),
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)validation.SecurityToken;
        jwt.GetClaim("azp").Value.Should().Be("simulator");
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("ShipperInvoices.Manage", "CarrierInvoices.Manage");
        jwt.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetToken_NoLocalTokensConfigured_Throws()
    {
        var tokens = new LocalErpTokens(Options.Create(new ErpApiOptions { BaseAddress = new Uri("https://erp.example") }), _time);

        var act = () => tokens.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ErpApi:LocalTokens*");
    }
}
