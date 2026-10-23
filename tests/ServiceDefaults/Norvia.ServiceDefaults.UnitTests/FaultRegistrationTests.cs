using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.ServiceDefaults.UnitTests;

public sealed class FaultRegistrationTests
{
    private static readonly FaultDefinition Unavailable = new("driver-app-unavailable", "driver-app", "Refuses every Trip.");

    private static ServiceProvider Register(Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.AddFaults(Unavailable);
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void AddFaults_GivesTheApplicationItsFaults_AllOff()
    {
        using var services = Register();

        services.GetRequiredService<IFaults>().List().Should().ContainSingle().Which.Should().Match<FaultStatus>(fault => fault.Name == "driver-app-unavailable" && !fault.On);
    }

    [Fact]
    public void AddFaults_AllowsAFewMinutesByDefault()
    {
        using var services = Register();

        var options = services.GetRequiredService<IOptions<FaultOptions>>().Value;

        options.MaxDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.DefaultDuration.Should().BeLessThanOrEqualTo(options.MaxDuration);
    }

    [Fact]
    public void AddFaults_TakesTheLimitFromConfiguration()
    {
        using var services = Register(new() { ["Faults:MaxDuration"] = "00:10:00" });

        services.GetRequiredService<IOptions<FaultOptions>>().Value.MaxDuration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddFaults_WithADefaultLongerThanTheMaximum_IsRefused()
    {
        using var services = Register(new() { ["Faults:MaxDuration"] = "00:01:00", ["Faults:DefaultDuration"] = "00:02:00" });

        var read = () => services.GetRequiredService<IOptions<FaultOptions>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }
}
