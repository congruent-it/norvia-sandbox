using Azure.Core;
using Norvia.Tms.Web.TmsApi;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

public sealed class EntraTmsApiTokensTests
{
    [Fact]
    public async Task GetTokenAsync_AsksTheTmsUisIdentityForATokenForTheTmsApi()
    {
        var identity = Substitute.For<TokenCredential>();
        identity.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("entra-token", DateTimeOffset.UtcNow.AddHours(1)));
        var tokens = new EntraTmsApiTokens(identity, "0b7c7d1e-tms-api/.default");

        var token = await tokens.GetTokenAsync(Xunit.TestContext.Current.CancellationToken);

        token.Should().Be("entra-token");
        await identity.Received().GetTokenAsync(
            Arg.Is<TokenRequestContext>(request => request.Scopes.SequenceEqual(new[] { "0b7c7d1e-tms-api/.default" })),
            Arg.Any<CancellationToken>());
    }
}
