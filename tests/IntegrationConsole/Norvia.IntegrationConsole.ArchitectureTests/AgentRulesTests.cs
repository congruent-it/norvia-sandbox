using System.Text.RegularExpressions;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;

namespace Norvia.IntegrationConsole.ArchitectureTests;

/// <summary>
/// The rules of the Agents that no one may break quietly (ADR 0040): an Agent has no write tool, and only Infrastructure projects
/// know the Foundry SDK. They read the repository's files, so a new Agent or project is held to them the day it is added.
/// </summary>
public sealed partial class AgentRulesTests
{
    // What reaches Foundry's models and agents from .NET.
    private static readonly string[] FoundrySdkPackages =
        ["Azure.AI.OpenAI", "Azure.AI.Projects", "Azure.AI.Agents", "Azure.AI.Agents.Persistent", "Azure.AI.Inference", "Microsoft.Extensions.AI.OpenAI", "Microsoft.Extensions.AI.AzureAIInference", "OpenAI"];

    private static readonly string[] WriteVerbs = ["create", "update", "delete", "remove", "set", "post", "put", "patch", "send", "write", "resubmit", "discard", "approve", "reject", "assign"];

    public static TheoryData<string> Definitions()
    {
        var files = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "agents"), "*.agent.yaml"))
        {
            files.Add(Path.GetFileName(file));
        }

        return files;
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void EveryAgentsToolList_IsReadOnly_AndNamesOnlyToolsItsServerServes(string fileName)
    {
        var definition = AgentDefinitionLoader.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "agents", fileName)));
        var contract = ToolListContract.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp", $"{definition.Tools.Server}.tools.json")));

        contract.Unknown(definition).Should().BeEmpty();
        definition.Tools.Names.Select(name => contract.Find(name)!).Should().OnlyContain(tool => tool.IsRead, "an Agent has no write tool (ADR 0040)");
        definition.Tools.Names.Should().NotContain(name => WriteVerbs.Any(verb => name.Split('_', StringSplitOptions.RemoveEmptyEntries).Contains(verb)), "a write tool needs its own ADR");
        fileName.Should().Be($"{definition.Name}.agent.yaml");
    }

    [Fact]
    public void EveryToolListInTheRepository_IsReadOnly()
    {
        var contracts = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "contracts", "agent-mcp"), "*.tools.json")
            .Select(file => ToolListContract.Parse(File.ReadAllText(file))).ToList();

        contracts.Should().NotBeEmpty();
        contracts.SelectMany(contract => contract.Tools).Should().OnlyContain(tool => tool.IsRead);
    }

    [Fact]
    public void OnlyInfrastructureProjects_ReferenceTheFoundrySdk()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => !Path.GetFileNameWithoutExtension(project).EndsWith(".Infrastructure", StringComparison.Ordinal))
            .Where(project => PackageReferences().Matches(File.ReadAllText(project)).Select(match => match.Groups["package"].Value).Any(FoundrySdkPackages.Contains))
            .Select(project => Path.GetRelativePath(RepositoryRoot.Path, project));

        offenders.Should().BeEmpty("only Infrastructure projects reference the Foundry SDK (ADR 0040)");
    }

    [Fact]
    public void TheFoundrySdk_IsReferencedByTheConsolesInfrastructure_SoTheRuleAboveHasSomethingToCatch()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/IntegrationConsole/Norvia.IntegrationConsole.Infrastructure/Norvia.IntegrationConsole.Infrastructure.csproj"));

        PackageReferences().Matches(project).Select(match => match.Groups["package"].Value).Should().Contain(FoundrySdkPackages.Take(1));
    }

    [Fact]
    public void TheConsolesApplicationAndWeb_DoNotLoadTheFoundrySdk()
    {
        var referenced = new[] { typeof(Application.Operators.OperatorRoles).Assembly, typeof(Web.Operators.OperatorSignIn).Assembly, typeof(Norvia.Agents.Suggestion).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies()).Select(name => name.Name ?? string.Empty);

        referenced.Should().NotContain(name => name.StartsWith("Azure.AI.", StringComparison.Ordinal) || name == "OpenAI" || name == "Microsoft.Extensions.AI.OpenAI");
    }

    [GeneratedRegex("""<PackageReference\s+Include="(?<package>[^"]+)""")]
    private static partial Regex PackageReferences();
}
