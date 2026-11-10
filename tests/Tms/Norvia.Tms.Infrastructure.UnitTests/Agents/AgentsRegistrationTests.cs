using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Infrastructure.Agents;

namespace Norvia.Tms.Infrastructure.UnitTests.Agents;

/// <summary>The Agents are off unless the AppHost or the estate turns them on, and settings that can't be right stop the TMS UI at startup (coding guidelines §8, ADR 0040).</summary>
public sealed class AgentsRegistrationTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Agents:Enabled"] = "true",
        ["Agents:McpAddress"] = "http://localhost:5196/mcp/customer-service",
        ["Agents:Tokens:Issuer"] = "https://norvia.local/tokens",
        ["Agents:Tokens:Audience"] = "api://norvia-customer-service-agent",
        ["Agents:Tokens:ClientId"] = "norvia-local-agent-customer-service",
        ["Agents:Tokens:Role"] = "TransportOrders.CustomerServiceAgent.Read",
        ["Agents:Tokens:SigningKey"] = "AAECAwQFBgcICQoLDA0ODw==",
    };

    [Fact]
    public async Task AddAgentClient_ByDefault_IsOff_AndAnswersUnavailable()
    {
        using var services = ServicesWith([]);

        var agents = services.GetRequiredService<IOptions<AgentsOptions>>().Value;
        var client = services.GetRequiredService<IAgentClient>();
        var result = await client.AskAsync(new AgentRequest("customer-service", "q", "c-1"), TestContext.Current.CancellationToken);

        agents.Enabled.Should().BeFalse();
        client.Should().BeOfType<DisabledAgentClient>();
        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public void AddAgentClient_WhenEnabled_RunsTheAgentInProcess()
    {
        using var services = ServicesWith(Complete);

        var start = () => Start(services);

        start.Should().NotThrow();
        services.GetRequiredService<IOptions<AgentsOptions>>().Value.Enabled.Should().BeTrue();
        services.GetRequiredService<IAgentClient>().Should().BeOfType<LocalAgentClient>();
    }

    [Fact]
    public void AddAgentClient_WhenGivenAFoundryProject_AsksTheAgentPublishedThere_WithoutAStandInOrALocalToken()
    {
        using var services = ServicesWith(new Dictionary<string, string?>
        {
            ["Agents:Enabled"] = "true",
            ["Agents:Foundry:ProjectEndpoint"] = "https://ais-norvia.services.ai.azure.com/api/projects/norvia",
        });

        var start = () => Start(services);

        start.Should().NotThrow();
        services.GetRequiredService<IAgentClient>().Should().BeOfType<FoundryAgentClient>();
    }

    [Fact]
    public void AddAgentClient_WhenTheFoundryProjectIsNotAnAbsoluteAddress_DoesNotStart()
    {
        using var services = ServicesWith(new Dictionary<string, string?>(Complete) { ["Agents:Foundry:ProjectEndpoint"] = "/api/projects/norvia" });

        var start = () => Start(services);

        start.Should().Throw<OptionsValidationException>().WithMessage("*Agents:Foundry:ProjectEndpoint*");
    }

    [Theory]
    [InlineData("Agents:McpAddress", "/mcp", "Agents:McpAddress")]
    [InlineData("Agents:McpAddress", "", "Agents:McpAddress")]
    [InlineData("Agents:Tokens:SigningKey", "", "Agents:Tokens")]
    [InlineData("Agents:Tokens:Role", "", "Agents:Tokens")]
    [InlineData("Agents:TimeoutSeconds", "0", "TimeoutSeconds")]
    public void AddAgentClient_WhenEnabledWithSettingsThatCantBeRight_DoesNotStart(string setting, string value, string named)
    {
        using var services = ServicesWith(new Dictionary<string, string?>(Complete) { [setting] = value });

        var start = () => Start(services);

        start.Should().Throw<OptionsValidationException>().WithMessage($"*{named}*");
    }

    private static void Start(ServiceProvider services) => services.GetRequiredService<IStartupValidator>().Validate();

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddAgentClient();
        return builder.Services.BuildServiceProvider();
    }
}

public sealed class AgentModelsAndDefinitionsTests
{
    [Fact]
    public void Load_TheCustomerServiceDefinition_ReadsTheEmbeddedFile()
    {
        var definition = AgentDefinitions.Load("customer-service");

        definition.Name.Should().Be("customer-service");
        definition.Tools.Names.Should().Equal("get_rejected_booking", "get_transport_order", "get_shipment_timeline", "get_exceptions", "get_proof_of_delivery_status", "get_release_eligibility");
    }

    [Fact]
    public void Load_AnAgentWithNoDefinition_Throws()
    {
        var load = () => AgentDefinitions.Load("no-such-agent");

        load.Should().Throw<InvalidAgentDefinitionException>();
    }

    [Fact]
    public void For_WithoutAModelEndpoint_IsTheScriptedFake()
    {
        var definition = AgentDefinitions.Load("customer-service");

        using var model = AgentModels.For(definition, new AgentModelOptions());

        model.Should().BeOfType<Norvia.Agents.Scripts.ScriptedChatClient>();
    }

    [Fact]
    public void For_AnAgentWithNoScript_AndNoModelEndpoint_Throws()
    {
        var definition = AgentDefinitions.Load("customer-service") with { Name = "another-agent" };

        var create = () => AgentModels.For(definition, new AgentModelOptions());

        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("my-own-deployment")]
    public void For_WithAModelEndpoint_IsADeploymentInFoundry(string? deployment)
    {
        var definition = AgentDefinitions.Load("customer-service");
        var options = new AgentModelOptions { Endpoint = new Uri("https://my-foundry.openai.azure.com/"), Deployment = deployment };

        using var model = AgentModels.For(definition, options);

        model.Should().NotBeOfType<Norvia.Agents.Scripts.ScriptedChatClient>();
    }

    [Fact]
    public void Create_AnAgentToken_HoldsTheIdentityAndTheRole_ForNineMinutesOrMore()
    {
        var tokens = new AgentTokenOptions
        {
            Issuer = "https://norvia.local/tokens",
            Audience = "api://a",
            ClientId = "agent-1",
            Role = "TransportOrders.CustomerServiceAgent.Read",
            SigningKey = Convert.ToBase64String(new byte[64]),
        };

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(AgentToken.Create(tokens, TimeProvider.System));

        token.GetClaim("azp").Value.Should().Be("agent-1");
        token.ValidTo.Should().BeAfter(DateTime.UtcNow.AddMinutes(9));
        tokens.IsComplete.Should().BeTrue();
    }
}
