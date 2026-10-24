namespace Norvia.Agents.Tools;

/// <summary>One tool of an MCP server, as the contract file lists it.</summary>
/// <param name="Name">The tool's name as the model sees it.</param>
/// <param name="Path">The operation's route on the application's agent view; <c>{name}</c> segments are the tool's inputs.</param>
public sealed record ToolSpec(string Name, string Description, string Method, string Path)
{
    /// <summary>The names of the <c>{placeholders}</c> in <see cref="Path"/>, in order: the tool's required inputs.</summary>
    public IReadOnlyList<string> Parameters { get; } = [.. System.Text.RegularExpressions.Regex.Matches(Path, @"\{([A-Za-z][A-Za-z0-9]*)\}").Select(match => match.Groups[1].Value)];

    /// <summary>An Agent has no write tools (ADR 0040): a tool is a GET.</summary>
    public bool IsRead => string.Equals(Method, "GET", StringComparison.Ordinal);
}
