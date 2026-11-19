using System.Text.RegularExpressions;

namespace Norvia.ServiceDefaults.UnitTests;

/// <summary>
/// The local sign-in and the local token-signing key run outside Development only through <c>LocalAuth:Allowed</c>. A sign-in class that tests
/// <c>IsDevelopment()</c> itself would keep refusing on Kubernetes, or drift from the one named setting, so each goes through <c>LocalAuth</c>.
/// </summary>
public sealed partial class LocalAuthGuardTests
{
    private static readonly string Source = Path.Combine(RepositoryRoot(), "src");

    [GeneratedRegex(@"Environment\.IsDevelopment\(\)")]
    private static partial Regex DevelopmentCheck();

    [Fact]
    public void NoSignInClass_ChecksTheEnvironmentNameItself()
    {
        var offenders = SignInSources().Where(file => DevelopmentCheck().IsMatch(File.ReadAllText(file))).Select(Path.GetFileName).ToList();

        offenders.Should().BeEmpty("a local sign-in is refused through LocalAuth.Require, so LocalAuth:Allowed is the one opt-in");
    }

    [Fact]
    public void EverySignInClass_GoesThroughLocalAuth()
    {
        var offenders = SignInSources().Where(file => !File.ReadAllText(file).Contains("LocalAuth.Require(", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToList();

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Tms/Norvia.Tms.Api/Program.cs")]
    [InlineData("Erp/Norvia.Erp.Api/Program.cs")]
    [InlineData("DriverApp/Norvia.DriverApp.Api/Program.cs")]
    public void EveryApiThatTrustsTheLocalKey_RefusesItUnlessLocalAuthPermitsIt(string program) =>
        File.ReadAllText(Path.Combine(Source, program)).Should().Contain("LocalAuth.RequireForSigningKeys(");

    private static IEnumerable<string> SignInSources() =>
        Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(Path.GetFileName(file), "^(OperatorSignIn|FinanceSignIn|DriverSignIn)\\.cs$"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
