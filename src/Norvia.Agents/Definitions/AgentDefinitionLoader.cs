using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Norvia.Agents.Definitions;

/// <summary>Reads an Agent's definition file (<c>agents/&lt;name&gt;.agent.yaml</c>) and checks it says everything a runner needs.</summary>
public static partial class AgentDefinitionLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static AgentDefinition Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        File file;
        try
        {
            file = Yaml.Deserialize<File?>(yaml) ?? throw new InvalidAgentDefinitionException("The definition is empty.");
        }
        catch (YamlException exception)
        {
            throw new InvalidAgentDefinitionException($"The definition isn't valid YAML: {exception.Message}", exception);
        }

        var name = Required(file.Name, "name");
        if (!NamePattern().IsMatch(name))
        {
            throw new InvalidAgentDefinitionException($"name '{name}' must be lower-case letters, digits and hyphens.");
        }

        if (file.Version < 1)
        {
            throw new InvalidAgentDefinitionException("version must be 1 or more.");
        }

        var model = file.Model ?? throw new InvalidAgentDefinitionException("model is missing.");
        if (model.MaxToolCalls < 1 || model.MaxOutputTokens < 1)
        {
            throw new InvalidAgentDefinitionException("model.maxToolCalls and model.maxOutputTokens must be 1 or more.");
        }

        // An Agent that only generates text has no tools (the Simulator's personas), so the section may be left out. A section that is
        // there still has to list at least one tool, each once.
        var tools = ToolsOf(file.Tools);

        return new AgentDefinition(
            name,
            file.Version,
            Required(file.Description, "description"),
            Required(file.Instructions, "instructions"),
            new ModelSettings(Required(model.Deployment, "model.deployment"), model.Temperature, model.MaxOutputTokens, model.MaxToolCalls),
            tools,
            tools.Names.Count == 0 ? new RunInputs(string.Empty) : new RunInputs(Required(file.Inputs?.CorrelationIdHeader, "inputs.correlationIdHeader")));
    }

    private static ToolSelection ToolsOf(FileTools? tools)
    {
        if (tools is null)
        {
            return new ToolSelection(string.Empty, []);
        }

        var names = tools.Names ?? [];
        if (names.Count == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw new InvalidAgentDefinitionException("tools.names must list at least one tool, each once.");
        }

        return new ToolSelection(Required(tools.Server, "tools.server"), [.. names]);
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidAgentDefinitionException($"{field} is missing.") : value.Trim();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]+$")]
    private static partial Regex NamePattern();

    // The file's own shape, so a missing field is told apart from a default.
    private sealed class File
    {
        public string? Name { get; set; }

        public int Version { get; set; }

        public string? Description { get; set; }

        public string? Instructions { get; set; }

        public FileModel? Model { get; set; }

        public FileTools? Tools { get; set; }

        public FileInputs? Inputs { get; set; }
    }

    private sealed class FileModel
    {
        public string? Deployment { get; set; }

        public double Temperature { get; set; }

        public int MaxOutputTokens { get; set; }

        public int MaxToolCalls { get; set; }
    }

    private sealed class FileTools
    {
        public string? Server { get; set; }

        public List<string>? Names { get; set; }
    }

    private sealed class FileInputs
    {
        public string? CorrelationIdHeader { get; set; }
    }
}
