using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Infrastructure.Agents;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Agents;

/// <summary>The Agents are off unless the AppHost or the estate turns them on, and settings that can't be right stop the app at startup (coding guidelines §8, ADR 0040).</summary>
public sealed class AgentsRegistrationTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Agents:Enabled"] = "true",
        ["Agents:McpAddress"] = "http://localhost:5196/mcp/health-check",
        ["Agents:Tokens:Issuer"] = "https://norvia.local/tokens",
        ["Agents:Tokens:Audience"] = "api://norvia-health-check-agent",
        ["Agents:Tokens:ClientId"] = "norvia-local-agent-estate-health-check",
        ["Agents:Tokens:Role"] = "HealthCheck.Agent.Read",
        ["Agents:Tokens:SigningKey"] = "AAECAwQFBgcICQoLDA0ODw==",
    };

    [Fact]
    public async Task AddAgentClient_ByDefault_IsOff_AndAnswersUnavailable()
    {
        using var services = ServicesWith([]);

        var agents = services.GetRequiredService<IOptions<AgentsOptions>>().Value;
        var client = services.GetRequiredService<IAgentClient>();
        var result = await client.NarrateAsync(new AgentRequest("estate-health-check", "q", "c-1"), TestContext.Current.CancellationToken);

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
        builder.Services.AddSingleton(Substitute.For<TokenCredential>());
        builder.AddAgentClient();
        return builder.Services.BuildServiceProvider();
    }
}

public sealed class AgentModelsAndDefinitionsTests
{
    [Fact]
    public void Load_TheEstateHealthCheckDefinition_ReadsTheEmbeddedFile()
    {
        var definition = AgentDefinitions.Load("estate-health-check");

        definition.Name.Should().Be("estate-health-check");
        definition.Tools.Server.Should().Be("health-check");
        definition.Tools.Names.Should().Equal("run_checks");
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
        var definition = AgentDefinitions.Load("estate-health-check");

        using var model = AgentModels.For(definition, new AgentModelOptions(), Substitute.For<TokenCredential>());

        model.Should().BeOfType<Norvia.Agents.Scripts.ScriptedChatClient>();
    }

    [Fact]
    public void For_AnAgentWithNoScript_AndNoModelEndpoint_Throws()
    {
        var definition = AgentDefinitions.Load("estate-health-check") with { Name = "another-agent" };

        var create = () => AgentModels.For(definition, new AgentModelOptions(), Substitute.For<TokenCredential>());

        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("my-own-deployment")]
    public void For_WithAModelEndpoint_IsADeploymentInFoundry(string? deployment)
    {
        var definition = AgentDefinitions.Load("estate-health-check");
        var options = new AgentModelOptions { Endpoint = new Uri("https://my-foundry.openai.azure.com/"), Deployment = deployment };

        using var model = AgentModels.For(definition, options, Substitute.For<TokenCredential>());

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
            Role = "HealthCheck.Agent.Read",
            SigningKey = Convert.ToBase64String(new byte[64]),
        };

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(AgentToken.Create(tokens, TimeProvider.System));

        token.GetClaim("azp").Value.Should().Be("agent-1");
        token.ValidTo.Should().BeAfter(DateTime.UtcNow.AddMinutes(9));
        tokens.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void NewRunIds_AreUnique()
    {
        var ids = new GuidRunIds();

        ids.Next().Should().NotBe(ids.Next());
    }
}
