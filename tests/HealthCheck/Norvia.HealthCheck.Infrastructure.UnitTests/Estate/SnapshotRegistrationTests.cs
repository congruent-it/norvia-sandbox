using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Infrastructure.Cost;
using Norvia.HealthCheck.Infrastructure.Estate;

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
    public async Task AddHealthCheckInfrastructure_ReadsTheCostFromTheModelledSnapshot_WhichTheEstateSettingChooses()
    {
        using var services = ServicesWith(new Dictionary<string, string?> { ["Estate:CostSnapshot"] = "chatty-telemetry" });

        var days = await services.GetRequiredService<ICostReader>().ReadAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), TestContext.Current.CancellationToken);

        days.Should().ContainSingle().Which.Spend.Should().Contain(spend => spend.Resource == "log-workspace" && spend.Eur == 0.53m);
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

public sealed class AzureRegistrationTests
{
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";

    [Fact]
    public void AddHealthCheckInfrastructure_WithAnEstateInAzure_ReadsTheRealEstate_AndNeedsNoSnapshot()
    {
        using var services = ServicesWith(new Dictionary<string, string?>
        {
            ["Estate:ResourceGroupId"] = ResourceGroupId,
            ["Estate:ServiceBusNamespace"] = "sb-norvia-abc",
            ["TmsApi:BaseAddress"] = "http://tms-api",
            ["TmsApi:Scope"] = "api://norvia-tms/.default",
        });

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().NotThrow();
        services.GetRequiredService<IEstateConfiguration>().Should().BeOfType<AzureEstateConfiguration>();
        services.GetRequiredService<ICostReader>().Should().BeOfType<CostManagementCostReader>();
    }

    [Fact]
    public void AddHealthCheckInfrastructure_WithAnEstateInAzureButNoTmsApi_DoesNotStart()
    {
        using var services = ServicesWith(new Dictionary<string, string?>
        {
            ["Estate:ResourceGroupId"] = ResourceGroupId,
            ["Estate:ServiceBusNamespace"] = "sb-norvia-abc",
        });

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().WithMessage("*BaseAddress*");
    }

    [Fact]
    public void AddHealthCheckInfrastructure_WithAnEstateInAzureButNoNamespace_DoesNotStart()
    {
        using var services = ServicesWith(new Dictionary<string, string?> { ["Estate:ResourceGroupId"] = ResourceGroupId });

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().WithMessage("*ServiceBusNamespace*");
    }

    [Fact]
    public void AddHealthCheckInfrastructure_WithTheAppsManagedIdentity_CallsAzureAsIt()
    {
        using var services = ServicesWith(new Dictionary<string, string?> { ["AZURE_CLIENT_ID"] = "00000000-0000-0000-0000-0000000000bb" });

        services.GetRequiredService<Azure.Core.TokenCredential>().Should().BeOfType<Azure.Identity.ManagedIdentityCredential>();
    }

    [Fact]
    public void AddHealthCheckInfrastructure_WithoutAManagedIdentity_CallsAzureAsTheDevelopersSignIn()
    {
        using var services = ServicesWith([]);

        services.GetRequiredService<Azure.Core.TokenCredential>().Should().BeOfType<Azure.Identity.DefaultAzureCredential>();
    }

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddHealthCheckInfrastructure();
        return builder.Services.BuildServiceProvider();
    }
}
