using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Infrastructure.Audit;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.DeadLetters;

/// <summary>In Azure the console lists the estate's namespace through Azure Resource Manager; locally, the emulators the AppHost gives it.</summary>
public sealed class DeadLetterQueuesRegistrationTests
{
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithoutLocalEmulators_ListsTheEstatesNamespace()
    {
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = ResourceGroupId, ["Estate:ServiceBusNamespace"] = "sb-norvia-abc" });

        services.GetRequiredService<IDeadLetterQueues>().Should().BeOfType<ServiceBusDeadLetterQueues>();
        services.GetRequiredService<IServiceBusEntities>().Should().BeOfType<ArmServiceBusEntities>();
        services.GetRequiredService<IOptions<EstateServiceBusOptions>>().Value.ServiceBusNamespace.Should().Be("sb-norvia-abc");
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_Always_WritesTheAuditThroughOpenTelemetry()
    {
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = ResourceGroupId, ["Estate:ServiceBusNamespace"] = "sb-norvia-abc" });

        services.GetRequiredService<IAuditLog>().Should().BeOfType<OpenTelemetryAuditLog>();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithLocalEmulators_ListsTheEmulatorsEntities()
    {
        using var services = ServicesWith(new()
        {
            ["LocalServiceBus:Namespaces:messaging:ConnectionString"] = "Endpoint=sb://localhost;",
            ["LocalServiceBus:Namespaces:messaging:ManagementConnectionString"] = "Endpoint=sb://localhost:5300;",
        });

        services.GetRequiredService<IServiceBusEntities>().Should().BeOfType<EmulatorServiceBusEntities>();
        services.GetRequiredService<IOptions<LocalServiceBusOptions>>().Value.Namespaces["messaging"].ManagementConnectionString
            .Should().Be("Endpoint=sb://localhost:5300;");
    }

    [Fact]
    public async Task Create_ForALocalEmulator_ConnectsByItsConnectionString_AndForTheEstateAsTheConsolesCredential()
    {
        var credential = Substitute.For<TokenCredential>();
        var local = Options.Create(new LocalServiceBusOptions
        {
            Namespaces = { ["messaging"] = new LocalServiceBusNamespace { ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true;" } },
        });
        var clients = new ServiceBusClients(credential, local);

        await using var emulator = clients.Create("messaging");
        await using var estate = clients.Create("sb-norvia-abc");

        emulator.FullyQualifiedNamespace.Should().Be("localhost");
        estate.FullyQualifiedNamespace.Should().Be("sb-norvia-abc.servicebus.windows.net");
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithoutAnEstate_StillStarts()
    {
        using var services = ServicesWith([]);

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().NotThrow();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithAResourceGroupIdThatIsNone_DoesNotStart()
    {
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = "rg-norvia" });

        var start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().WithMessage("*Estate:ResourceGroupId*");
    }

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddIntegrationConsoleInfrastructure();
        builder.Services.RemoveAll<TokenCredential>();
        builder.Services.AddSingleton(Substitute.For<TokenCredential>());
        return builder.Services.BuildServiceProvider();
    }
}
