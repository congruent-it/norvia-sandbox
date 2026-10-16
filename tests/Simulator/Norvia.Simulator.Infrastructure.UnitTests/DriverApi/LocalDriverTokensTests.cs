using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Simulator.Infrastructure.DriverApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.DriverApi;

public sealed class LocalDriverTokensTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 5)).ToArray());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));

    private LocalDriverTokens Tokens(LocalDriverTokenOptions? local = null) => new(
        Options.Create(new DriverApiOptions
        {
            BaseAddress = new Uri("https://driver.example"),
            LocalTokens = local ?? new LocalDriverTokenOptions { Issuer = "https://issuer", Audience = "api://driver", ClientId = "simulator", SigningKey = Key },
        }),
        _time);

    [Fact]
    public async Task GetToken_IsSignedAsTheEstateWouldIssueItToTheDriver_WithTheDriverIdAsTheOneRole()
    {
        var token = await Tokens().GetTokenAsync("DRV-0003", CancellationToken.None);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer",
            ValidAudience = "api://driver",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Key)),
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)validation.SecurityToken;
        jwt.GetClaim("azp").Value.Should().Be("simulator");
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("DRV-0003");
        jwt.ValidTo.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task GetResetToken_IsSignedAsTheSimulator_WithTheResetRole()
    {
        var token = await Tokens().GetResetTokenAsync(CancellationToken.None);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.GetPayloadValue<string[]>("roles").Should().Equal("Estate.Reset");
        jwt.GetClaim("azp").Value.Should().Be("simulator");
    }

    [Fact]
    public async Task GetToken_ForADifferentDriver_HoldsThatDriversRole()
    {
        var token = await Tokens().GetTokenAsync("DRV-0006", CancellationToken.None);

        new JsonWebTokenHandler().ReadJsonWebToken(token).GetPayloadValue<string[]>("roles").Should().Equal("DRV-0006");
    }

    [Fact]
    public async Task GetToken_WithoutLocalTokenOptions_Throws()
    {
        var tokens = new LocalDriverTokens(Options.Create(new DriverApiOptions { BaseAddress = new Uri("https://driver.example") }), _time);

        var act = () => tokens.GetTokenAsync("DRV-0003", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DriverApp:LocalTokens*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetToken_WithoutADriver_Throws(string driverId)
    {
        var act = () => Tokens().GetTokenAsync(driverId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
