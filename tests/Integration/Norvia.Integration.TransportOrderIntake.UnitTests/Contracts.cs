using System.Reflection;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>The files in contracts/, embedded in the test assembly under their path, such as <c>PartnerApi/booking.v1.schema.json</c>.</summary>
internal static class Contracts
{
    private static readonly Assembly Assembly = typeof(Contracts).Assembly;

    /// <summary>The names of the files directly in a folder, without the folder.</summary>
    public static IEnumerable<string> FileNames(string folder) =>
        Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(folder + "/", StringComparison.Ordinal) && !name[(folder.Length + 1)..].Contains('/', StringComparison.Ordinal))
            .Select(name => name[(folder.Length + 1)..]);

    public static string Read(string path)
    {
        using var stream = Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException($"{path} is not an embedded contract.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
