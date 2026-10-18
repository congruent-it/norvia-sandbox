using System.Security.Claims;
using Microsoft.Extensions.Options;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests;

public sealed class IntakeCallersTests
{
    private static readonly IntakeCallersOptions ShipperApiCaller = new()
    {
        ClientIdsByChannel = new() { [IntakeChannel.ShipperApi] = ["6F1C2B7A-0000-4000-8000-000000000001"] },
    };

    [Theory]
    [InlineData("azp")]
    [InlineData("appid")]
    public void TryFindChannel_RegisteredClientIdInEitherClaim_FindsItsChannel(string claim)
    {
        var callers = new IntakeCallers(Options.Create(ShipperApiCaller));

        var found = callers.TryFindChannel(Caller(claim, "6f1c2b7a-0000-4000-8000-000000000001"), out var channel);

        found.Should().BeTrue();
        channel.Should().Be(IntakeChannel.ShipperApi);
    }

    [Fact]
    public void TryFindChannel_UnregisteredClientId_FindsNone()
    {
        var callers = new IntakeCallers(Options.Create(ShipperApiCaller));

        callers.TryFindChannel(Caller("azp", "some-other-app"), out _).Should().BeFalse();
    }

    [Fact]
    public void RegistersEachCallerOnce_CallerOnOneChannel_IsTrue()
    {
        ShipperApiCaller.RegistersEachCallerOnce().Should().BeTrue();
    }

    [Fact]
    public void RegistersEachCallerOnce_CallerListedTwiceInAnyCase_IsFalse()
    {
        var options = new IntakeCallersOptions
        {
            ClientIdsByChannel = new() { [IntakeChannel.ShipperApi] = ["intake-app", "INTAKE-APP"] },
        };

        options.RegistersEachCallerOnce().Should().BeFalse();
    }

    private static ClaimsPrincipal Caller(string claim, string clientId) =>
        new(new ClaimsIdentity([new Claim(claim, clientId)], "Bearer"));
}
