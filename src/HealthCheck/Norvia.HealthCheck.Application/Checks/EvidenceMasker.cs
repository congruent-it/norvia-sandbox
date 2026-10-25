using System.Text.RegularExpressions;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// Keeps a secret's value out of a Finding. Evidence says where a secret is (a setting's name, a resource), never what it is; this is the
/// second line, for a value that gets into a detail anyway: whatever follows a secret-looking name, and any bearer token.
/// </summary>
public static partial class EvidenceMasker
{
    public const string Mask = "***";

    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Bearer().Replace(SecretValue().Replace(text, match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{Mask}"), $"Bearer {Mask}");
    }

    [GeneratedRegex(@"(?<name>[\w.-]*(?:password|secret|key|token|credential|connectionstring)[\w.-]*)(?<separator>\s*[=:]\s*)(?<value>""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretValue();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();
}
