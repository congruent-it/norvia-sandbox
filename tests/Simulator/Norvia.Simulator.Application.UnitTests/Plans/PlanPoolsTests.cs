using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Simulator.Application.Plans.Pools;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Plans;

/// <summary>A plan carries pools of Carrier texts: built-in by default, the agent's when it is on and well behaved, and never a failed plan.</summary>
public sealed class PlanPoolsTests
{
    private static readonly PersonaProfile Dutch = new("terse-dutch", "nl", "blunt");
    private static readonly PersonaProfile German = new("german", "de", "plain");

    private static readonly string[] BuiltIn = ["Eén", "Twee", "Drie", "Vier"];

    private static readonly string[] Kept = ["Geen plek", "Vol", "Niet mogelijk"];

    private readonly IPersonaProfiles _profiles = Substitute.For<IPersonaProfiles>();
    private readonly IBuiltInPools _builtIn = Substitute.For<IBuiltInPools>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    public PlanPoolsTests()
    {
        _profiles.ProfileOf("CAR-0001").Returns(Dutch);
        _profiles.ProfileOf("CAR-0002").Returns(German);
        _builtIn.TextsOf(Arg.Any<PersonaProfile>(), Arg.Any<TextCategory>()).Returns(BuiltIn);
    }

    private static SimulationPlan Plan(int runNumber = 7) => BookingPlanGenerator.Generate(TestData.Settings(), TestData.World, runNumber);

    private PlanPools Pools(bool enabled = false, int cap = 8) =>
        new(_profiles, _builtIn, _agent, new PersonaSettings(enabled, cap, PoolSize: 5), NullLogger<PlanPools>.Instance);

    private static Result<IReadOnlyList<string>> Texts(params string[] texts) => Result<IReadOnlyList<string>>.Success(texts);

    [Fact]
    public async Task AddTo_AgentsOff_GivesEveryProfileAndKindItsBuiltInPoolAndCallsNoAgent()
    {
        var plan = await Pools().AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().HaveCount(4).And.OnlyContain(pool => pool.Source == "built-in" && pool.Texts.SequenceEqual(BuiltIn));
        plan.CarrierProfiles.Should().Equal(new Dictionary<string, string> { ["CAR-0001"] = "terse-dutch", ["CAR-0002"] = "german" });
        plan.PoolNotes.Should().BeEmpty();
        await _agent.DidNotReceiveWithAnyArgs().GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTo_AgentsOn_KeepsTheAgentsValidTextsAndSaysWhichRunWroteThem()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns(Texts("Geen plek", "Vol", "Niet mogelijk", "Helaas niet"));

        var plan = await Pools(enabled: true).AddToAsync(Plan(7), TestData.World, CancellationToken.None);

        plan.Pools.Should().HaveCount(4).And.OnlyContain(pool => pool.Source == "agent, run 7" && pool.Texts.Count == 4);
        plan.PoolNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddTo_AskingTheAgent_TellsItOnlyTheProfileTheKindAndTheCount()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns(Texts("a b", "c d", "e f"));

        await Pools(enabled: true).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        await _agent.Received(1).GeneratePoolAsync(new PoolRequest(Dutch, TextCategory.DeclineReason, 5), Arg.Any<CancellationToken>());
        await _agent.Received(1).GeneratePoolAsync(new PoolRequest(German, TextCategory.DeliveryFailedRemark, 5), Arg.Any<CancellationToken>());
        typeof(PoolRequest).GetProperties().Select(property => property.PropertyType).Should().BeEquivalentTo([typeof(PersonaProfile), typeof(TextCategory), typeof(int)]);
    }

    [Fact]
    public async Task AddTo_ATextThatFailsValidation_IsDroppedAndTheRestKept()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>())
            .Returns(Texts("Geen plek", "Bel 0612345678 voor info", "zie www.example.com", "Vol", "Niet mogelijk"));

        var plan = await Pools(enabled: true).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().OnlyContain(pool => pool.Texts.SequenceEqual(Kept));
    }

    [Fact]
    public async Task AddTo_APoolLeftTooSmall_FallsBackToTheBuiltInPoolWithANote()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns(Texts("Vol", "Bel 0612345678", "https://x.nl"));

        var plan = await Pools(enabled: true).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().OnlyContain(pool => pool.Source == "built-in");
        plan.PoolNotes.Should().HaveCount(4).And.OnlyContain(note => note.Contains("only 1 usable"));
    }

    [Fact]
    public async Task AddTo_TheModelFailing_FallsBackToTheBuiltInPoolWithAVisibleNoteAndTheNoteNamesNoText()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<string>>.Unavailable("AgentUnavailable"));

        var plan = await Pools(enabled: true).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().HaveCount(4).And.OnlyContain(pool => pool.Source == "built-in");
        plan.PoolNotes.Should().HaveCount(4).And.OnlyContain(note => note.Contains("the built-in pool is used"));
    }

    [Fact]
    public async Task AddTo_TheAgentThrowing_IsTheSameAsItFailing_AndNeverBlocksThePlan()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns<Result<IReadOnlyList<string>>>(_ => throw new InvalidOperationException("boom"));

        var plan = await Pools(enabled: true).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().HaveCount(4).And.OnlyContain(pool => pool.Source == "built-in");
        plan.PoolNotes.Should().HaveCount(4);
    }

    [Fact]
    public async Task AddTo_TheCapOnGenerations_LeavesTheRestBuiltIn()
    {
        _agent.GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>()).Returns(Texts("Geen plek", "Vol", "Niet mogelijk"));

        var plan = await Pools(enabled: true, cap: 3).AddToAsync(Plan(), TestData.World, CancellationToken.None);

        await _agent.ReceivedWithAnyArgs(3).GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>());
        plan.Pools.Count(pool => pool.Source != "built-in").Should().Be(3);
        plan.Pools.Count(pool => pool.Source == "built-in").Should().Be(1);
        plan.PoolNotes.Should().ContainSingle().Which.Should().Contain("cap of 3");
    }

    [Fact]
    public async Task AddTo_NoBuiltInPool_LeavesThatCarriersFixedWordsWithANote()
    {
        _builtIn.TextsOf(German, Arg.Any<TextCategory>()).Returns([]);

        var plan = await Pools().AddToAsync(Plan(), TestData.World, CancellationToken.None);

        plan.Pools.Should().OnlyContain(pool => pool.ProfileId == "terse-dutch");
        plan.CarrierProfiles.Keys.Should().Equal("CAR-0001");
        plan.PoolNotes.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddTo_CarriersWithoutAProfile_LeaveThePlanAsItWas()
    {
        _profiles.ProfileOf(Arg.Any<string>()).Returns((PersonaProfile?)null);
        var before = Plan();

        var plan = await Pools(enabled: true).AddToAsync(before, TestData.World, CancellationToken.None);

        plan.Should().BeSameAs(before);
        await _agent.DidNotReceiveWithAnyArgs().GeneratePoolAsync(Arg.Any<PoolRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTo_TheSamePlanTwice_GivesTheSamePoolsAndTheSameDraws()
    {
        var first = await Pools().AddToAsync(Plan(), TestData.World, CancellationToken.None);
        var second = await Pools().AddToAsync(Plan(), TestData.World, CancellationToken.None);

        second.Pools.Should().BeEquivalentTo(first.Pools);
        first.PoolFor("CAR-0001", TextCategory.DeclineReason)!.Draw(11, 3).Text.Should().Be(second.PoolFor("CAR-0001", TextCategory.DeclineReason)!.Draw(11, 3).Text);
    }

    [Fact]
    public async Task AddTo_APlanWithPools_StoresAndReadsBackAsJsonWithNoModel()
    {
        var plan = await Pools().AddToAsync(Plan(), TestData.World, CancellationToken.None);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

        var back = System.Text.Json.JsonSerializer.Deserialize<SimulationPlan>(System.Text.Json.JsonSerializer.Serialize(plan, options), options)!;

        back.Pools.Should().BeEquivalentTo(plan.Pools);
        back.CarrierProfiles.Should().Equal(plan.CarrierProfiles);
        back.PoolFor("CAR-0002", TextCategory.DeliveryFailedRemark)!.Draw(5, 2).Text.Should().Be(plan.PoolFor("CAR-0002", TextCategory.DeliveryFailedRemark)!.Draw(5, 2).Text);
    }
}
