using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The local sign-in and the local token-signing key run outside Development only through <c>LocalAuth:Allowed</c> (ADR 0053). A sign-in class that
/// tests <c>IsDevelopment()</c> itself would keep refusing on Kubernetes or drift from the one named setting, so each goes through <c>LocalAuth</c>.
/// </summary>
public sealed partial class LocalAuthTests
{
    private static readonly string Source = Path.Combine(RepositoryRoot.Path, "src");

    [GeneratedRegex(@"Environment\.IsDevelopment\(\)")]
    private static partial Regex DevelopmentCheck();

    [GeneratedRegex("^(OperatorSignIn|FinanceSignIn|DriverSignIn)\\.cs$")]
    private static partial Regex SignInFile();

    [Fact]
    public void TheSignInClasses_AreFound() => SignInSources().Should().HaveCount(5, "the Integration console, Health check, Simulator, ERP and Driver app");

    [Fact]
    public void NoSignInClass_ChecksTheEnvironmentNameItself()
    {
        var offenders = SignInSources().Where(file => DevelopmentCheck().IsMatch(File.ReadAllText(file))).Select(Path.GetFileName).ToList();

        offenders.Should().BeEmpty("a local sign-in is refused through LocalAuth.Require, so LocalAuth:Allowed is the one opt-in");
    }

    [Fact]
    public void EverySignInClass_GoesThroughLocalAuth()
    {
        var offenders = SignInSources()
            .Where(file => !File.ReadAllText(file).Contains("LocalAuth.Require(", StringComparison.Ordinal)
                || !File.ReadAllText(file).Contains("LocalAuth.RequireLocalSignInWhenAllowed(", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToList();

        offenders.Should().BeEmpty("each sign-in refuses outside Development unless allowed, and distrusts Easy Auth while allowed");
    }

    [Fact]
    public void EveryApiProgramThatSetsUpJwtBearer_RefusesTheLocalKeyUnlessLocalAuthPermitsIt()
    {
        var offenders = Directory.EnumerateFiles(Source, "Program.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => (File: file, Text: File.ReadAllText(file)))
            .Where(found => found.Text.Contains("AddJwtBearer", StringComparison.Ordinal))
            .Where(found => !found.Text.Contains("LocalAuth.RequireForSigningKeys(", StringComparison.Ordinal))
            .Select(found => Path.GetRelativePath(Source, found.File))
            .ToList();

        offenders.Should().BeEmpty("an API that takes signing keys from configuration must refuse the local key outside Development unless LocalAuth:Allowed is set");
    }

    private static IEnumerable<string> SignInSources() =>
        Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => SignInFile().IsMatch(Path.GetFileName(file)));

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
