using System.Text.RegularExpressions;

namespace Norvia.ServiceDefaults.UnitTests;

/// <summary>
/// No fault endpoint may be mapped without the guard (coding guidelines §8). The guard is built into <c>MapFaults</c>, which is the only way to
/// expose a fault, so this reads the applications' source and fails when an endpoint is mapped by hand on a fault route, as the Driver app's
/// anonymous <c>/unavailable</c> switch once was.
/// </summary>
public sealed partial class FaultEndpointGuardTests
{
    private static readonly string Source = Path.Combine(RepositoryRoot(), "src");

    [GeneratedRegex("""\.Map(?:Get|Put|Post|Delete|Patch|Methods|Fallback)\(\s*(?:HttpMethods\.\w+,\s*)?"[^"]*(?:fault|unavailable)[^"]*""", RegexOptions.IgnoreCase)]
    private static partial Regex HandMappedFaultRoute();

    [GeneratedRegex("""MapGroup\(\s*"[^"]*fault[^"]*"[^;]*;""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FaultRouteGroup();

    [Fact]
    public void NoApplication_MapsAFaultEndpointByHand()
    {
        var offenders = ApplicationSources().Where(file => HandMappedFaultRoute().IsMatch(File.ReadAllText(file))).ToList();

        offenders.Should().BeEmpty("a fault switch is mapped with MapFaults, which requires the Demo.Fault role");
    }

    [Fact]
    public void EveryFaultRouteGroup_IsMappedWithMapFaults()
    {
        var offenders = ApplicationSources()
            .SelectMany(file => FaultRouteGroup().Matches(File.ReadAllText(file)).Select(match => (File: file, Group: match.Value)))
            .Where(found => !found.Group.Contains("MapFaults()", StringComparison.Ordinal))
            .Select(found => found.File)
            .ToList();

        offenders.Should().BeEmpty("a route group for faults must end in MapFaults(), so its endpoints carry the guard");
    }

    [Fact]
    public void TheGuardsOwnSource_IsFound()
    {
        // A scan that finds nothing proves nothing: the Driver app maps its faults, so the scan must have seen that.
        ApplicationSources().Any(file => FaultRouteGroup().IsMatch(File.ReadAllText(file))).Should().BeTrue();
    }

    private static IEnumerable<string> ApplicationSources() =>
        Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains(Path.Combine("Norvia.ServiceDefaults", "Faults"), StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Norvia.slnx isn't above the test run.");
    }
}
