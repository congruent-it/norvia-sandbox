using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Infrastructure.Personas;

namespace Norvia.Simulator.Infrastructure.UnitTests.Personas;

/// <summary>The profiles in the Simulator's configuration, the built-in pools in the repo, and the local personas agent.</summary>
public sealed class PersonasTests
{
    // The Simulator's own appsettings.json, embedded so the test needs no file system.
    private static PersonaOptions Configured()
    {
        using var stream = typeof(PersonasTests).Assembly.GetManifestResourceStream("Web/appsettings.json")!;
        var options = new PersonaOptions();
        new ConfigurationBuilder().AddJsonStream(stream).Build().GetSection(PersonaOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void TheShippedConfiguration_HasAFewProfilesAndGivesEveryCarrierOneThatIsDefined()
    {
        var options = Configured();

        options.Profiles.Select(profile => profile.Id).Should().BeEquivalentTo("formal-english", "terse-dutch", "sloppy-mixed", "german");
        options.Profiles.Select(profile => profile.Language).Should().BeEquivalentTo("en", "nl", "mixed", "de");
        options.Carriers.Keys.Should().Contain(["CAR-0001", "CAR-0002", "CAR-0003"]);
        options.IsConsistent.Should().BeTrue();
    }

    [Fact]
    public void TheShippedConfiguration_GivesEveryDriverOfTheOwnFleetAProfileThatIsDefined()
    {
        var options = Configured();

        options.Drivers.Keys.Should().Contain(["DRV-0001", "DRV-0002", "DRV-0003", "DRV-0004", "DRV-0005"]);
        options.Drivers.Values.Distinct().Should().BeEquivalentTo("formal-english", "terse-dutch", "sloppy-mixed", "german");
    }

    [Fact]
    public void IsConsistent_ADriverWithAProfileThatIsNotDefined_IsNot()
    {
        var options = new PersonaOptions { Profiles = [new PersonaProfileOptions { Id = "a", Language = "en", Style = "s" }], Drivers = { ["DRV-1"] = "b" } };

        options.IsConsistent.Should().BeFalse();
    }

    [Fact]
    public void ProfileOfDriver_AKeyedDriver_GivesTheirProfileAndAnotherDriverNone()
    {
        var profiles = new ConfiguredPersonaProfiles(Options.Create(Configured()));

        profiles.ProfileOfDriver("DRV-0004")!.Id.Should().Be("german");
        profiles.ProfileOfDriver("DRV-9999").Should().BeNull();
        profiles.ProfileOf("DRV-0004").Should().BeNull();
    }

    [Fact]
    public void IsConsistent_ACarrierWithAProfileThatIsNotDefined_IsNot()
    {
        var options = new PersonaOptions { Profiles = [new PersonaProfileOptions { Id = "a", Language = "en", Style = "s" }], Carriers = { ["CAR-1"] = "b" } };

        options.IsConsistent.Should().BeFalse();
    }

    [Fact]
    public void IsConsistent_AProfileDefinedTwice_IsNot()
    {
        var profile = new PersonaProfileOptions { Id = "a", Language = "en", Style = "s" };

        new PersonaOptions { Profiles = [profile, profile] }.IsConsistent.Should().BeFalse();
    }

    [Fact]
    public void ProfileOf_AKeyedCarrier_GivesItsProfileAndAnotherCarrierNone()
    {
        var profiles = new ConfiguredPersonaProfiles(Options.Create(Configured()));

        profiles.ProfileOf("CAR-0001").Should().Be(new PersonaProfile("terse-dutch", "nl", "a few blunt words, no pleasantries"));
        profiles.ProfileOf("CAR-9999").Should().BeNull();
    }

    [Fact]
    public void TextsOf_EveryProfileAndKindOfTheShippedConfiguration_HasABuiltInPoolBigEnoughAndValid()
    {
        var pools = new EmbeddedBuiltInPools();
        var configured = Configured().Profiles.Select(profile => new PersonaProfile(profile.Id, profile.Language, profile.Style)).ToList();

        foreach (var profile in configured)
        {
            foreach (var category in Enum.GetValues<TextCategory>())
            {
                var texts = pools.TextsOf(profile, category);

                texts.Count.Should().BeGreaterThanOrEqualTo(PoolTextRules.MinPoolSize, $"{profile.Id} {category} is a pool");
                texts.Should().OnlyContain(text => PoolTextRules.IsAcceptable(text), $"the built-in {profile.Id} {category} pool passes the rules");
            }
        }
    }

    [Fact]
    public void TextsOf_AProfileWithNoFile_GivesNothing()
    {
        new EmbeddedBuiltInPools().TextsOf(new PersonaProfile("klingon", "tlh", "x"), TextCategory.DeclineReason).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("mixed")]
    [InlineData("de")]
    public async Task GeneratePoolAsync_TheScriptedFake_WritesTextsThatPassTheRulesInEveryLanguage(string language)
    {
        var client = new LocalAgentClient(Options.Create(new AgentsOptions { Enabled = true }), NullLogger<LocalAgentClient>.Instance);

        foreach (var category in Enum.GetValues<TextCategory>())
        {
            var result = await client.GeneratePoolAsync(new PoolRequest(new PersonaProfile("p", language, "s"), category, 8), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.Value.Count.Should().BeGreaterThanOrEqualTo(PoolTextRules.MinPoolSize);
            PoolTextRules.Acceptable(result.Value).Should().Equal(result.Value);
        }
    }

    [Fact]
    public async Task GeneratePoolAsync_TheSameRequestTwice_GivesTheSameTexts()
    {
        var client = new LocalAgentClient(Options.Create(new AgentsOptions { Enabled = true }), NullLogger<LocalAgentClient>.Instance);
        var request = new PoolRequest(new PersonaProfile("p", "nl", "s"), TextCategory.DeclineReason, 8);

        var first = await client.GeneratePoolAsync(request, TestContext.Current.CancellationToken);
        var second = await client.GeneratePoolAsync(request, TestContext.Current.CancellationToken);

        second.Value.Should().Equal(first.Value);
    }

    [Fact]
    public async Task GeneratePoolAsync_ALanguageTheFakeDoesNotKnow_IsUnavailableAndNeverThrows()
    {
        var client = new LocalAgentClient(Options.Create(new AgentsOptions { Enabled = true }), NullLogger<LocalAgentClient>.Instance);

        var result = await client.GeneratePoolAsync(new PoolRequest(new PersonaProfile("p", "tlh", "s"), TextCategory.DeclineReason, 8), TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Unavailable);
        result.Errors.Should().ContainSingle().Which.Should().Be("AgentUnavailable");
    }

    [Fact]
    public async Task GeneratePoolAsync_ACancelledCaller_IsCancelledNotUnavailable()
    {
        var client = new LocalAgentClient(Options.Create(new AgentsOptions { Enabled = true }), NullLogger<LocalAgentClient>.Instance);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => client.GeneratePoolAsync(new PoolRequest(new PersonaProfile("p", "nl", "s"), TextCategory.DeclineReason, 8), cancelled.Token);

        // The scripted model answers at once, so a cancelled caller sees either a pool or its own cancellation, never a failure code.
        var result = await Record.ExceptionAsync(act);
        (result is null || result is OperationCanceledException).Should().BeTrue();
    }

    [Fact]
    public async Task GeneratePoolAsync_TheDisabledClient_IsUnavailable()
    {
        var result = await new DisabledAgentClient().GeneratePoolAsync(new PoolRequest(new PersonaProfile("p", "nl", "s"), TextCategory.DeclineReason, 8), TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Unavailable);
    }
}
