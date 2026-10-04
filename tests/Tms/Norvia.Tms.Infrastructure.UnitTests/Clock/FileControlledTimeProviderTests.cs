using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Norvia.Tms.Infrastructure.Clock;

namespace Norvia.Tms.Infrastructure.UnitTests.Clock;

/// <summary>The clock a test host sets by writing a file, and the only hosts that may have it.</summary>
public sealed class FileControlledTimeProviderTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"norvia-clock-{Guid.NewGuid():N}.txt");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void GetUtcNow_WithTheFileWritten_IsTheInstantInIt()
    {
        File.WriteAllText(_file, "2026-10-05T10:00:00.0000000+02:00");

        new FileControlledTimeProvider(_file).GetUtcNow().Should().Be(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetUtcNow_IsReadOnEveryCall_SoATestCanMoveTheClock()
    {
        var clock = new FileControlledTimeProvider(_file);
        File.WriteAllText(_file, "2026-10-05T10:00:00Z");
        var first = clock.GetUtcNow();

        File.WriteAllText(_file, "2026-10-05T10:30:00Z");

        clock.GetUtcNow().Should().Be(first.AddMinutes(30));
    }

    [Fact]
    public void GetUtcNow_WithNoFile_IsTheSystemsTime()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new FileControlledTimeProvider(_file).GetUtcNow();

        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GetUtcNow_WithTheFilesFolderMissing_IsTheSystemsTime()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new FileControlledTimeProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "clock.txt")).GetUtcNow();

        now.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void ClockFor_ControlFileNamed_ControlsTheClockOnlyInDevelopment(string environmentName, bool controlled)
    {
        File.WriteAllText(_file, "2001-01-01T00:00:00Z");
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Clock:ControlFile"] = _file }).Build();

        var clock = DependencyInjection.ClockFor(environment, configuration);

        (clock.GetUtcNow().Year == 2001).Should().Be(controlled);
    }

    [Fact]
    public void ClockFor_NoControlFileNamed_IsTheSystemClock()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        DependencyInjection.ClockFor(environment, new ConfigurationBuilder().Build()).Should().BeSameAs(TimeProvider.System);
    }
}
