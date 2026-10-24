using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

public sealed class ToolListContractTests
{
    [Fact]
    public void Parse_TheIntegrationOperationsToolList_HasTheTwoReadTools()
    {
        var contract = ToolListContract.Parse(Embedded.IntegrationOperationsTools);

        contract.Server.Should().Be("integration-operations");
        contract.Role.Should().Be("IntegrationConsole.Agent.Read");
        contract.Tools.Select(tool => tool.Name).Should().Equal("get_message_trail", "get_run");
        contract.Find("get_run")!.Parameters.Should().Equal("logicApp", "workflow", "runId");
    }

    [Fact]
    public void TheIntegrationOperationsToolList_IsReadOnly_AndHasNoToolTheDefinitionLacksNorTheOtherWayRound()
    {
        var contract = ToolListContract.Parse(Embedded.IntegrationOperationsTools);
        var definition = AgentDefinitionLoader.Parse(Embedded.IntegrationOperationsDefinition);

        contract.Tools.Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tools (ADR 0040)");
        contract.Unknown(definition).Should().BeEmpty();
        definition.Tools.Server.Should().Be(contract.Server);
        contract.Tools.Select(tool => tool.Name).Should().BeEquivalentTo(definition.Tools.Names);
    }

    [Fact]
    public void Unknown_ADefinitionListingAToolTheServerDoesNotServe_NamesIt()
    {
        var contract = ToolListContract.Parse(Embedded.IntegrationOperationsTools);
        var definition = AgentDefinitionLoader.Parse(Embedded.IntegrationOperationsDefinition);
        definition = definition with { Tools = definition.Tools with { Names = ["get_run", "delete_run"] } };

        contract.Unknown(definition).Should().Equal("delete_run");
    }

    [Fact]
    public void IsRead_AToolThatIsNotAGet_IsFalse()
    {
        new ToolSpec("delete_run", "Deletes.", "DELETE", "/runs/{id}").IsRead.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"server\":\"x\",\"role\":\"A.B\",\"tools\":[]}")]
    [InlineData("{\"server\":\"x\",\"role\":\"A.B\",\"tools\":[{\"name\":\"a\",\"path\":\"no-slash\"}]}")]
    [InlineData("null")]
    public void Parse_ABrokenToolList_IsRefused(string json)
    {
        var act = () => ToolListContract.Parse(json);

        act.Should().Throw<InvalidAgentDefinitionException>();
    }
}
