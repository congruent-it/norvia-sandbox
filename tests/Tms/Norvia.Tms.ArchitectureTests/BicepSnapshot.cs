using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// Evaluates one of the estate's Bicep modules locally with <c>bicep snapshot</c>, which predicts the resources a deployment would
/// create; nothing is deployed.
/// </summary>
internal static class BicepSnapshot
{
    /// <summary>The resources the module at <paramref name="module"/> (relative to the repository root) predicts for the given parameter lines.</summary>
    public static List<JsonNode> Resources(string module, params string[] parameterLines)
    {
        var directory = Directory.CreateTempSubdirectory("bicep-snapshot-");
        try
        {
            var parameters = Path.Combine(directory.FullName, "module.bicepparam");
            File.WriteAllText(parameters, $"using '{RelativeTo(directory.FullName, Path.Combine(RepositoryRoot.Path, module))}'\n{string.Join('\n', parameterLines)}\n");

            Run(directory.FullName, "snapshot", "module.bicepparam", "--mode", "overwrite",
                "--subscription-id", "00000000-0000-0000-0000-000000000000", "--resource-group", "rg", "--location", "westeurope");

            var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(directory.FullName, "module.snapshot.json")))!;
            return snapshot["predictedResources"]!.AsArray().Select(resource => resource!).ToList();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // Bicep refuses an absolute path in a parameters file.
    public static string RelativeTo(string directory, string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');

    public static void Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(BicepPath(), arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, $"bicep {string.Join(' ', arguments)} printed: {output.Result}{error.Result}");
    }

    // `az bicep install` puts the Bicep CLI in ~/.azure/bin, which CI does too.
    private static string BicepPath()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure", "bin", OperatingSystem.IsWindows() ? "bicep.exe" : "bicep");
        return File.Exists(installed) ? installed : "bicep";
    }
}
