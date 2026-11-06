using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;

namespace Norvia.Agents.UnitTests;

/// <summary>The Simulator's personas agent: no tools, one JSON object of texts, and a scripted fake that is deterministic.</summary>
public sealed class PersonaAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.SimulatorPersonasDefinition);

    private static AgentRunner Runner(Microsoft.Extensions.AI.IChatClient? model = null) =>
        new(model ?? PersonaTextScript.Model(), NoTools.Source, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));

    private static AgentQuestion Ask(string language, string category) =>
        new(PersonaQuestions.Ask("profile-x", "short", language, category, 5), CorrelationId: string.Empty, "run-1");

    [Fact]
    public void Parse_ThePersonasDefinition_HasNoToolsAndSaysWhatToWrite()
    {
        Definition.Name.Should().Be("simulator-personas");
        Definition.Tools.Names.Should().BeEmpty();
        Definition.Tools.Server.Should().BeEmpty();
        Definition.Instructions.Should().Contain("texts").And.Contain("web address").And.Contain("invent nothing");
    }

    [Fact]
    public void Parse_AnAgentWithoutAToolsSection_IsAllowed_ButASectionWithNoToolsIsNot()
    {
        const string Head = "name: writer\nversion: 1\ndescription: d\ninstructions: i\nmodel:\n  deployment: m\n  temperature: 0.5\n  maxOutputTokens: 10\n  maxToolCalls: 1\n";

        AgentDefinitionLoader.Parse(Head).Tools.Names.Should().BeEmpty();
        var act = () => AgentDefinitionLoader.Parse(Head + "tools:\n  server: s\n  names: []\n");
        act.Should().Throw<InvalidAgentDefinitionException>();
    }

    [Theory]
    [InlineData("en", "DeclineReason")]
    [InlineData("nl", "DeliveryFailedRemark")]
    [InlineData("mixed", "DeclineReason")]
    [InlineData("de", "DeliveryFailedRemark")]
    public async Task RunTextsAsync_TheScriptedFake_ReturnsAPoolInThatLanguageWithoutCallingATool(string language, string category)
    {
        var result = await Runner().RunTextsAsync(Definition, Ask(language, category), TestContext.Current.CancellationToken);

        result.RunId.Should().Be("run-1");
        result.Texts.Should().HaveCountGreaterThanOrEqualTo(3).And.OnlyContain(text => text.Length > 0);
    }

    [Fact]
    public async Task RunTextsAsync_TheSameQuestion_GivesTheSameTexts()
    {
        var first = await Runner().RunTextsAsync(Definition, Ask("nl", "DeclineReason"), TestContext.Current.CancellationToken);
        var second = await Runner().RunTextsAsync(Definition, Ask("nl", "DeclineReason"), TestContext.Current.CancellationToken);

        second.Texts.Should().Equal(first.Texts);
    }

    [Fact]
    public async Task RunTextsAsync_AQuestionTheFakeCannotRead_IsAnUnavailableAgent()
    {
        var act = () => Runner().RunTextsAsync(Definition, Ask("klingon", "DeclineReason"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task RunTextsAsync_AModelThatAsksForATool_IsRefusedBecauseTheDefinitionListsNone()
    {
        var model = new ScriptedChatClient(context => [new Microsoft.Extensions.AI.FunctionCallContent("c", "list_things")]);

        var act = () => Runner(model).RunTextsAsync(Definition, Ask("nl", "DeclineReason"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData("""{"texts":["a","b"]}""", 2)]
    [InlineData("```json\n{\"texts\":[\"a\"]}\n```", 1)]
    public void Parse_AListOfTexts_IsRead(string reply, int count) => TextsParser.Parse(reply).Should().HaveCount(count);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"texts":"a"}""")]
    [InlineData("""{"texts":[1,2]}""")]
    public void Parse_AnythingElse_IsAnUnavailableAgent(string reply)
    {
        var act = () => TextsParser.Parse(reply);

        act.Should().Throw<AgentRunException>().Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public void TryParse_AQuestionWithoutALanguageOrACategory_IsNotRead()
    {
        PersonaQuestions.TryParse("hello", out _, out _).Should().BeFalse();
        PersonaQuestions.TryParse(PersonaQuestions.Ask("p", "s", "nl", "DeclineReason", 3), out var language, out var category).Should().BeTrue();
        (language, category).Should().Be(("nl", "DeclineReason"));
    }

    [Fact]
    public void Ask_TheQuestion_CarriesTheProfileTheLanguageAndTheCategoryAndNothingElse()
    {
        var question = PersonaQuestions.Ask("terse-dutch", "blunt", "nl", "DeclineReason", 8);

        question.Should().Be("Write 8 texts.\nprofile: terse-dutch\nstyle: blunt\nlanguage: nl\ncategory: DeclineReason");
    }
}
