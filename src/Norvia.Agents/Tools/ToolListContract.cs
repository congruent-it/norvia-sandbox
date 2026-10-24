using System.Text.Json;
using Norvia.Agents.Definitions;

namespace Norvia.Agents.Tools;

/// <summary>
/// The tool list of one MCP server (<c>contracts/agent-mcp/&lt;server&gt;.tools.json</c>, ADR 0040). The Bicep module reads
/// the file to build the API Management MCP server and the local MCP stand-in reads it to serve the same tools, so the two
/// cannot drift.
/// </summary>
/// <param name="Role">The one read role an Agent's token must carry.</param>
public sealed record ToolListContract(string Server, string Role, IReadOnlyList<ToolSpec> Tools)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ToolSpec? Find(string name) => Tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    public static ToolListContract Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        File file;
        try
        {
            file = JsonSerializer.Deserialize<File>(json, Json)
                ?? throw new InvalidAgentDefinitionException("The tool list is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidAgentDefinitionException($"The tool list isn't valid JSON: {exception.Message}", exception);
        }

        if (string.IsNullOrWhiteSpace(file.Server) || string.IsNullOrWhiteSpace(file.Role) || file.Tools is not { Count: > 0 })
        {
            throw new InvalidAgentDefinitionException("The tool list needs a server, a role and at least one tool.");
        }

        var tools = file.Tools.Select(tool =>
            string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Path) || !tool.Path.StartsWith('/')
                ? throw new InvalidAgentDefinitionException("Every tool needs a name and a path starting with '/'.")
                : new ToolSpec(tool.Name, tool.Description ?? string.Empty, tool.Method ?? string.Empty, tool.Path)).ToList();
        return new ToolListContract(file.Server, file.Role, tools);
    }

    /// <summary>The names of the tools in the definition that the contract doesn't have: a definition may list only tools its server serves.</summary>
    public IReadOnlyList<string> Unknown(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return [.. definition.Tools.Names.Where(name => Find(name) is null)];
    }

    private sealed class File
    {
        public string? Server { get; set; }

        public string? Role { get; set; }

        public List<FileTool>? Tools { get; set; }
    }

    private sealed class FileTool
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Method { get; set; }

        public string? Path { get; set; }
    }
}
