using System.Reflection;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft.UnitTests;

/// <summary>Only a host's Infrastructure references the Foundry SDK (ADR 0040, ADR 0041): the Agents' library and the Booking draft library do not.</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] CloudSdkPrefixes = ["Azure.", "Microsoft.Azure", "Microsoft.Agents", "OpenAI"];

    [Theory]
    [InlineData(typeof(BookingDraftExtractor))]
    [InlineData(typeof(AgentRunner))]
    public void TheLibrary_ReferencesNoAzureOrFoundrySdk(Type type)
    {
        var references = type.Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

        references.Should().NotContain(name => CloudSdkPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheIntakeAgent_HasNoTools_SoItCanReadNothingOfTheEstate()
    {
        var definition = BookingIntakeDefinition.Load();

        definition.Tools.Names.Should().BeEmpty();
        definition.Name.Should().Be("booking-intake");
    }
}
