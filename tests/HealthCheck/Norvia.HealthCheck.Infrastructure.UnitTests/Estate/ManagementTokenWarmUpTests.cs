using Azure.Core;
using Microsoft.Extensions.Logging.Testing;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

public sealed class ManagementTokenWarmUpTests
{
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();
    private readonly FakeLogger<ManagementTokenWarmUp> _logger = new();

    [Fact]
    public async Task Start_AsksForTheTokenForAzureResourceManager()
    {
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("t", DateTimeOffset.MaxValue));
        using var warmUp = new ManagementTokenWarmUp(_credential, _logger);

        await warmUp.StartAsync(TestContext.Current.CancellationToken);
        await warmUp.ExecuteTask!;

        await _credential.Received(1).GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.SequenceEqual(new[] { "https://management.azure.com/.default" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_WhenTheTokenCannotBeFetched_OnlyLogsIt()
    {
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns<AccessToken>(_ => throw new InvalidOperationException("no identity"));
        using var warmUp = new ManagementTokenWarmUp(_credential, _logger);

        await warmUp.StartAsync(TestContext.Current.CancellationToken);
        await warmUp.ExecuteTask!;

        warmUp.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Collector.GetSnapshot().Should().ContainSingle().Which.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
    }
}
