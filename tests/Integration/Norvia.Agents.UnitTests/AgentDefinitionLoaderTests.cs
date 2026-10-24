using Norvia.Agents.Definitions;

namespace Norvia.Agents.UnitTests;

public sealed class AgentDefinitionLoaderTests
{
    private const string Valid = """
        name: things-agent
        version: 2
        description: Explains things.
        instructions: Be brief.
        model:
          deployment: small
          temperature: 0.5
          maxOutputTokens: 100
          maxToolCalls: 3
        tools:
          server: things
          names: [list_things, get_thing]
        inputs:
          correlationIdHeader: X-Correlation-ID
        """;

    [Fact]
    public void Parse_TheIntegrationOperationsDefinition_SaysItsToolsAndItsInstructions()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded.IntegrationOperationsDefinition);

        definition.Name.Should().Be("integration-operations");
        definition.Tools.Server.Should().Be("integration-operations");
        definition.Tools.Names.Should().Equal("get_message_trail", "get_run", "list_failed_runs", "list_dead_letter_queues", "peek_dead_letters");
        definition.Inputs.CorrelationIdHeader.Should().Be("X-Correlation-ID");
        definition.Model.MaxToolCalls.Should().BePositive();
        definition.Instructions.Should().Contain("data", because: "tool results are delimited data (ADR 0040)")
            .And.Contain("tool_result")
            .And.Contain("cites", because: "every claim cites the result it came from");
    }

    [Fact]
    public void Parse_AValidFile_ReadsEveryField()
    {
        var definition = AgentDefinitionLoader.Parse(Valid);

        definition.Should().BeEquivalentTo(new AgentDefinition(
            "things-agent",
            2,
            "Explains things.",
            "Be brief.",
            new ModelSettings("small", 0.5, 100, 3),
            new ToolSelection("things", ["list_things", "get_thing"]),
            new RunInputs("X-Correlation-ID")));
    }

    [Theory]
    [InlineData("name: things-agent", "name: Things")]
    [InlineData("version: 2", "version: 0")]
    [InlineData("description: Explains things.", "description: ''")]
    [InlineData("instructions: Be brief.", "instructions: ''")]
    [InlineData("deployment: small", "deployment: ''")]
    [InlineData("maxToolCalls: 3", "maxToolCalls: 0")]
    [InlineData("names: [list_things, get_thing]", "names: []")]
    [InlineData("names: [list_things, get_thing]", "names: [list_things, list_things]")]
    [InlineData("server: things", "server: ''")]
    [InlineData("correlationIdHeader: X-Correlation-ID", "correlationIdHeader: ''")]
    public void Parse_AFileWithAnInvalidField_IsRefused(string field, string replacement)
    {
        var act = () => AgentDefinitionLoader.Parse(Valid.Replace(field, replacement, StringComparison.Ordinal));

        act.Should().Throw<InvalidAgentDefinitionException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("name: [unclosed")]
    [InlineData("name: x-agent")]
    public void Parse_AFileThatIsEmptyBrokenOrIncomplete_IsRefused(string yaml)
    {
        var act = () => AgentDefinitionLoader.Parse(yaml);

        act.Should().Throw<InvalidAgentDefinitionException>();
    }

    [Fact]
    public void Parse_AFileWithoutItsModelToolsOrInputs_IsRefused()
    {
        foreach (var section in new[] { "model:", "tools:", "inputs:" })
        {
            var lines = Valid.Split('\n').TakeWhile(line => !line.StartsWith(section, StringComparison.Ordinal))
                .Concat(Valid.Split('\n').SkipWhile(line => !line.StartsWith(section, StringComparison.Ordinal)).Skip(1).SkipWhile(line => line.StartsWith("  ", StringComparison.Ordinal)));
            var act = () => AgentDefinitionLoader.Parse(string.Join('\n', lines));

            act.Should().Throw<InvalidAgentDefinitionException>(because: section);
        }
    }
}
