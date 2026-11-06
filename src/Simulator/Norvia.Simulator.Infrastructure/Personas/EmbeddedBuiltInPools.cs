using System.Reflection;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>
/// The built-in pools: curated files in the repo (<c>Personas/Pools/&lt;profile&gt;.&lt;category&gt;.&lt;language&gt;.txt</c>), one text a line, embedded in the
/// assembly. They are the default, what CI uses and what a failed generation falls back to.
/// </summary>
internal sealed class EmbeddedBuiltInPools : IBuiltInPools
{
    public IReadOnlyList<string> TextsOf(PersonaProfile profile, TextCategory category)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"pools/{profile.Id}.{category}.{profile.Language}.txt");
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return PoolTextRules.Acceptable(reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')));
    }
}
