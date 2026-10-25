using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

public sealed class SnapshotRegistrationTests
{
    [Fact]
    public async Task AddHealthCheckInfrastructure_ReadsTheSnapshot_AsTheLocalAdapterOfThePort()
    {
        using var services = ServicesWith(new Dictionary<string, string?> { ["Estate:Snapshot"] = "fixed" });

        var estate = await services.GetRequiredService<IEstateConfiguration>().ReadAsync(TestContext.Current.CancellationToken);

        estate.Name.Should().Be("fixed");
    }

    [Fact]
    public async Task AddHealthCheckInfrastructure_WithoutASetting_ReadsTheEstateAsFound()
    {
        using var services = ServicesWith([]);

        var estate = await services.GetRequiredService<IEstateConfiguration>().ReadAsync(TestContext.Current.CancellationToken);

        estate.Name.Should().Be("as-found");
    }

    [Fact]
    public void AddHealthCheckInfrastructure_WithASnapshotThatDoesNotExist_DoesNotStart()
    {
        using var services = ServicesWith(new Dictionary<string, string?> { ["Estate:Snapshot"] = "production" });

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().WithMessage("*Estate:Snapshot*");
    }

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddHealthCheckInfrastructure();
        return builder.Services.BuildServiceProvider();
    }
}
