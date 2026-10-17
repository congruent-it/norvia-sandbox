using Norvia.DriverApp.Infrastructure.Reset;

namespace Norvia.DriverApp.Infrastructure.UnitTests.Reset;

/// <summary>The quiet check of a process that can't peek a subscription, such as the Driver app's screens.</summary>
public sealed class UnavailableQuietCheckTests
{
    [Fact]
    public async Task FindBusySubscriptionAsync_Asked_RefusesInsteadOfClaimingTheEstateIsQuiet()
    {
        var check = new UnavailableQuietCheck();

        var act = () => check.FindBusySubscriptionAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
