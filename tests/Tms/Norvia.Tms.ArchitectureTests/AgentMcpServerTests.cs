using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The reusable Agent MCP server module (infra/estate/agent-mcp-server.bicep, ADR 0040) and its policy. The module is evaluated
/// locally with <c>bicep snapshot</c>, so the policy tested is the one a deployment would apply, with the module's values filled
/// in. API Management runs only in Azure; the post-deploy smoke run (infra/smoke-test-agent-mcp-server.ps1) calls the example server.
/// </summary>
public sealed class AgentMcpServerTests
{
    private const string Module = "infra/estate/agent-mcp-server.bicep";
    private const string ContractFolder = "contracts/agent-mcp";
    private const string Audience = "11111111-1111-1111-1111-111111111111";
    private const string Role = "Things.Read";

    private static readonly string[] Tools = ["list_things", "get_thing"];

    private static XElement Policy(params string[] parameters) => XElement.Parse(Single(Resources(parameters), "Microsoft.ApiManagement/service/apis/policies")["properties"]!["value"]!.GetValue<string>());

    private static XElement Inbound(params string[] parameters) => Policy(parameters).Element("inbound")!;

    private static string Status(XElement refusal) => refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value;

    private static bool HasMetadata(XElement trace, string name) => trace.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == name);

    private static string Metadata(XElement trace, string name) =>
        trace.Elements("metadata").Single(metadata => metadata.Attribute("name")!.Value == name).Attribute("value")!.Value;

    private static XElement Refusal(string failureCode, params string[] parameters) =>
        Inbound(parameters).Elements("choose").Elements("when").Single(when => when.Element("trace") is { } trace && trace.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == "failureCode" && metadata.Attribute("value")!.Value == failureCode));

    [Fact]
    public void Inbound_CreatesTheCorrelationIdBeforeAnyCheck()
    {
        var first = Inbound().Elements().First(element => element.Name != "base");

        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
    }

    [Fact]
    public void Inbound_ChecksInOrder_TheToken_TheRole_TheRateLimit_TheToolList_ThenRecordsTheHeaders()
    {
        var inbound = Inbound().Elements().Select(element => element.Name.LocalName == "choose" ? element.Element("when")!.Attribute("condition")!.Value : element.Name.LocalName + ":" + element.Attribute("name")?.Value).ToList();

        var token = inbound.FindIndex(step => step == "validate-azure-ad-token:");
        var role = inbound.FindIndex(step => step.Contains($"\"{Role}\"", StringComparison.Ordinal));
        var rate = inbound.FindIndex(step => step == "rate-limit-by-key:");
        var tool = inbound.FindIndex(step => step.Contains("tools/call", StringComparison.Ordinal));
        var headers = inbound.FindIndex(step => step == "set-header:X-Agent-ID");

        new[] { token, role, rate, tool, headers }.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.NotContain(-1);
    }

    [Fact]
    public void ValidToken_IsAnEntraTokenForTheAgentViewsAudience_AndOnlyThat()
    {
        var validate = Inbound().Element("validate-azure-ad-token")!;

        validate.Attribute("tenant-id")!.Value.Should().Be("tenant-1");
        validate.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().Equal(Audience);
        validate.Element("client-application-ids").Should().BeNull("any Agent with the role may call, whoever its application is");
    }

    [Fact]
    public void WrongAudience_IsRefusedByTheTokenCheck_WhichRunsBeforeAnythingElse()
    {
        // validate-azure-ad-token answers 401 itself; no other check runs first, and nothing in the policy skips it.
        var inbound = Inbound().Elements().Where(element => element.Name != "base").ToList();

        inbound.Select(element => element.Name.LocalName).Should().ContainInOrder("set-variable", "validate-azure-ad-token");
        Inbound().Element("validate-azure-ad-token")!.Attribute("ignore-error").Should().BeNull();
    }

    [Fact]
    public void WrongRole_IsRefusedWith403()
    {
        var refusal = Refusal("AgentRoleMissing");

        refusal.Attribute("condition")!.Value.Should().Contain($"\"{Role}\"").And.Contain("\"roles\"");
        Status(refusal).Should().Be("403");
        refusal.Element("return-response")!.Element("set-body")!.Value.Should().Contain("This agent may not use this server.");
        Metadata(refusal.Element("trace")!, "outcome").Should().Be("Refused");
    }

    [Fact]
    public void ATokenThatNamesNoCallingApplication_IsRefusedWith403()
    {
        var refusal = Refusal("NoAgentIdentity");

        Status(refusal).Should().Be("403");
        Inbound().Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "agentId").Attribute("value")!.Value.Should().Contain("\"azp\"");
    }

    [Fact]
    public void OverTheRateLimit_IsRefusedPerAgentIdentity_ByTheRateLimitPolicy()
    {
        var limit = Inbound("param rateLimitCalls = 7", "param rateLimitPeriodSeconds = 30").Element("rate-limit-by-key")!;

        limit.Attribute("counter-key")!.Value.Should().Contain("agentId");
        limit.Attribute("calls")!.Value.Should().Be("7");
        limit.Attribute("renewal-period")!.Value.Should().Be("30");
        Inbound().Element("rate-limit-by-key")!.Attribute("calls")!.Value.Should().Be("60");
    }

    [Fact]
    public void ATool_NotOnTheList_IsRefusedWith403_AndTheListIsTheModulesTools()
    {
        var refusal = Refusal("ToolNotOnList");

        Status(refusal).Should().Be("403");
        refusal.Attribute("condition")!.Value.Should().Contain("tools/call").And.Contain("\"list_things|get_thing\".Split('|')");
        Metadata(refusal.Element("trace")!, "toolName").Should().Contain("toolName");
        refusal.Element("return-response")!.Element("set-body")!.Value.Should().Contain("This tool is not available.");
    }

    [Fact]
    public void TheVariablesAndHeaders_NeverHoldAnEmptyValue_BecauseTheGatewayAnswers500ToOne()
    {
        var variables = Inbound().Elements("set-variable").Where(variable => variable.Attribute("name")!.Value is "mcpMethod" or "toolName");

        variables.Should().HaveCount(2).And.OnlyContain(variable => !variable.Attribute("value")!.Value.Contains("&quot;&quot;") && !variable.Attribute("value")!.Value.Contains("\"\""));
    }

    [Fact]
    public void TheToolNameHeader_IsDroppedFromTheCaller_AndSetOnlyWhenAToolIsNamed_BecauseAnEmptyValueMakesTheGatewayAnswer500()
    {
        var inbound = Inbound();

        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Tool-Name").Attribute("exists-action")!.Value.Should().Be("delete");
        var named = inbound.Elements("choose").Single(choose => choose.Descendants("set-header").Any(header => header.Attribute("name")!.Value == "X-Tool-Name")).Element("when")!;
        named.Attribute("condition")!.Value.Should().Contain("toolName").And.Contain("\"none\"");
        var header = named.Element("set-header")!;
        header.Attribute("exists-action")!.Value.Should().Be("override");
        header.Element("value")!.Value.Should().Contain("toolName");
    }

    [Fact]
    public void TheToolName_ComesFromTheMcpMessage_NotFromAHeaderTheCallerSets()
    {
        var toolName = Inbound().Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "toolName").Attribute("value")!.Value;

        toolName.Should().Contain("Request.Body").And.Contain("\"params\"").And.Contain("\"name\"").And.NotContain("Headers");
    }

    [Fact]
    public void TheHeaders_RecordTheCorrelationId_TheAgentsIdentityAndTheToolName_OverridingTheCallers()
    {
        var inbound = Inbound();

        Header(inbound, "X-Correlation-ID").Should().Contain("correlationId");
        Header(inbound, "X-Agent-ID").Should().Contain("agentId");
        inbound.Elements("set-header").Where(header => header.Attribute("name")!.Value is "X-Correlation-ID" or "X-Agent-ID")
            .Should().OnlyContain(header => header.Attribute("exists-action")!.Value == "override");
        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "Ocp-Apim-Subscription-Key").Attribute("exists-action")!.Value.Should().Be("delete");
        Policy().Element("outbound")!.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID").Element("value")!.Value.Should().Contain("correlationId");
    }

    [Fact]
    public void Traces_AreOnTheAgentFlow_AndRecordTheAgentTheToolAndTheRun_OnlyWhereTheyApply()
    {
        var policy = Policy("param serverName = 'things'");
        var accepted = policy.Element("inbound")!.Descendants("trace").Where(trace => Metadata(trace, "outcome") == "Succeeded").ToList();

        policy.Descendants("trace").Should().OnlyContain(trace => Metadata(trace, "flow") == "agent-mcp-things");
        accepted.Should().HaveCount(4, "one for each of: a tool and a run, a tool, a run, neither; a key that does not apply is left out, not sent with a stand-in value");
        accepted.Should().OnlyContain(trace => Metadata(trace, "agentId").Contains("agentId") && Metadata(trace, "correlationId").Contains("correlationId"));
        accepted.Where(trace => HasMetadata(trace, "toolName")).Should().HaveCount(2).And.OnlyContain(trace => Metadata(trace, "toolName").Contains("toolName"));
        accepted.Where(trace => HasMetadata(trace, "runId")).Should().HaveCount(2).And.OnlyContain(trace => Metadata(trace, "runId").Contains("runId"));
        accepted.Where(trace => HasMetadata(trace, "toolName") && HasMetadata(trace, "runId")).Should().ContainSingle();
        accepted.Where(trace => !HasMetadata(trace, "toolName") && !HasMetadata(trace, "runId")).Should().ContainSingle();
    }

    [Fact]
    public void TheRunId_IsWhatTheCallerSentAsAPlainIdentifier_ReadBeforeTheCorrelationHeaderIsOverridden_AndPassedToTheAppInItsOwnHeader()
    {
        var inbound = Inbound();
        var steps = inbound.Elements().ToList();
        var runId = inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "runId").Attribute("value")!.Value;

        runId.Should().Contain("X-Correlation-ID").And.Contain("Regex.IsMatch").And.Contain("\"none\"");
        steps.IndexOf(inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "runId"))
            .Should().BeLessThan(steps.IndexOf(inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID")), "the caller's own header is overridden after it is read");
        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Run-ID").Attribute("exists-action")!.Value.Should().Be("delete");
        var passed = inbound.Elements("choose").Single(choose => choose.Descendants("set-header").Any(header => header.Attribute("name")!.Value == "X-Run-ID")).Element("when")!;
        passed.Attribute("condition")!.Value.Should().Contain("runId").And.Contain("\"none\"");
        passed.Element("set-header")!.Element("value")!.Value.Should().Contain("runId");
    }

    [Fact]
    public void OnError_DoesNotRecordAClientClosingItsStream_AsAFailedHop()
    {
        var recorded = Policy().Element("on-error")!.Elements("choose").Single(choose => choose.Descendants("trace").Any()).Element("when")!;

        recorded.Attribute("condition")!.Value.Should().Contain("LastError.Reason").And.Contain("!=").And.Contain("ClientConnectionFailure");
        recorded.Element("trace")!.Should().NotBeNull();
    }

    [Fact]
    public void Policy_NeverReadsOrLogsTheResponseBody()
    {
        var text = Policy().ToString();

        text.Should().NotContain("Response.Body").And.NotContain("log-to-eventhub").And.NotContain("send-request");
    }

    [Fact]
    public void Policy_HasNoPlaceholderLeftOver()
    {
        Regex.IsMatch(Policy().ToString(), "__[A-Z_]+__").Should().BeFalse();
        Regex.IsMatch(Single(Resources(), "Microsoft.ApiManagement/service/apis/operations/policies")["properties"]!["value"]!.GetValue<string>(), "__[A-Z_]+__").Should().BeFalse();
    }

    [Fact]
    public void EveryTool_IsAReadOperationOfTheAgentView_AndAToolOfTheServer()
    {
        var resources = Resources();
        var operations = resources.Where(resource => Type(resource) == "Microsoft.ApiManagement/service/apis/operations").ToList();
        var tools = resources.Where(resource => Type(resource) == "Microsoft.ApiManagement/service/apis/tools").ToList();

        operations.Select(operation => operation["properties"]!["method"]!.GetValue<string>()).Should().OnlyContain(method => method == "GET");
        // The name the Agent sees is the display name; the resource's name carries the server's, as a tool's name is unique across the service.
        tools.Select(tool => tool["properties"]!["displayName"]!.GetValue<string>()).Should().BeEquivalentTo(Tools);
        tools.Select(tool => tool["name"]!.GetValue<string>().Split('/').Last()).Should().BeEquivalentTo(Tools.Select(tool => $"things-{tool}"));
        operations.Select(operation => operation["name"]!.GetValue<string>().Split('/').Last()).Should().BeEquivalentTo(Tools);
        resources.Single(resource => Type(resource) == "Microsoft.ApiManagement/service/apis" && resource["properties"]!["type"]?.GetValue<string>() == "mcp")["properties"]!["subscriptionRequired"]!.GetValue<bool>()
            .Should().BeFalse("the Agent's Entra token is its credential");
    }

    [Fact]
    public void TheModule_DeclaresNoDiagnostics_SoItCannotSwitchOnPayloadLogging()
    {
        Resources().Should().NotContain(resource => Type(resource).EndsWith("/diagnostics", StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, Module)).Should().NotContain("/diagnostics@");
    }

    [Fact]
    public void ABackedTool_CallsTheApplicationAsApiManagementsIdentity_WithAShortTimeoutAndNoRetry()
    {
        var operation = Single(Resources("param backendId = 'things-api'", "param backendAudience = 'api-audience'", "param apiManagementIdentityClientId = 'identity-client'"),
            "Microsoft.ApiManagement/service/apis/operations/policies");
        var policy = XElement.Parse(operation["properties"]!["value"]!.GetValue<string>());

        policy.Element("inbound")!.Element("set-backend-service")!.Attribute("backend-id")!.Value.Should().Be("things-api");
        policy.Element("inbound")!.Element("authentication-managed-identity")!.Attribute("resource")!.Value.Should().Be("api-audience");
        policy.Element("inbound")!.Element("authentication-managed-identity")!.Attribute("client-id")!.Value.Should().Be("identity-client");
        policy.Element("inbound")!.Element("validate-azure-ad-token")!.Element("required-claims")!.Element("claim")!.Element("value")!.Value.Should().Be(Role);
        int.Parse(policy.Element("backend")!.Element("forward-request")!.Attribute("timeout")!.Value, System.Globalization.CultureInfo.InvariantCulture).Should().BeInRange(1, 30);
        policy.Descendants("retry").Should().BeEmpty();
    }

    [Fact]
    public void AToolWithAMockResponse_IsAnsweredByApiManagementItself()
    {
        var tools = $$"""[{ name: 'ping', description: 'Pings.', method: 'GET', path: '/ping', mockResponse: { pong: true } }]""";
        var operation = Single(BicepSnapshot.Resources(Module, [.. Defaults(tools)]), "Microsoft.ApiManagement/service/apis/operations/policies");
        var policy = XElement.Parse(operation["properties"]!["value"]!.GetValue<string>());

        policy.Element("inbound")!.Element("return-response")!.Element("set-body")!.Value.Should().Contain("\"pong\"");
        policy.Descendants("set-backend-service").Should().BeEmpty();
    }

    [Fact]
    public void PayloadLoggingStaysOffAtTheServiceScope_AndNoDiagnosticsLogABody()
    {
        var estate = Path.Combine(RepositoryRoot.Path, "infra");
        var declarations = Directory.EnumerateFiles(estate, "*.bicep", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"(?ms)^resource \w+ 'Microsoft\.ApiManagement/service(/apis)?/diagnostics@[^']+' = \{.*?^\}$").Select(match => (file, block: match.Value)))
            .ToList();

        declarations.Should().NotBeEmpty("the service has its diagnostics");
        declarations.Should().OnlyContain(declaration => !Regex.IsMatch(declaration.block, @"\bbody\s*:"), "a body setting would log payloads, which an MCP server in API Management cannot have (ADR 0040)");
        declarations.Should().OnlyContain(declaration => !declaration.block.Contains("dataMasking", StringComparison.Ordinal) || !Regex.IsMatch(declaration.block, @"\bbody\s*:"));
    }

    [Fact]
    public void TheServiceDiagnostics_LogTheAgentAndToolHeadersOnTheBackendRequest()
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate/partner-api.bicep"));
        var block = Regex.Match(bicep, @"(?ms)^resource diagnostics 'Microsoft\.ApiManagement/service/diagnostics@[^']+' = \{.*?^\}$").Value;

        block.Should().Contain("'X-Agent-ID'").And.Contain("'X-Tool-Name'").And.Contain("'X-Correlation-ID'");
    }

    public static TheoryData<string> ContractFiles()
    {
        var files = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, ContractFolder), "*.tools.json"))
        {
            files.Add(Path.GetFileName(file));
        }

        return files;
    }

    [Theory]
    [MemberData(nameof(ContractFiles))]
    public void ToolListContract_HasTheSameShapeForEveryServer_AndOnlyReadTools(string fileName)
    {
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, ContractFolder, fileName)))!;

        contract["server"]!.GetValue<string>().Should().Be(fileName.Replace(".tools.json", "", StringComparison.Ordinal)).And.MatchRegex("^[a-z0-9][a-z0-9-]+$");
        contract["role"]!.GetValue<string>().Should().MatchRegex(@"^[A-Za-z]+(\.[A-Za-z]+)+$");
        var tools = contract["tools"]!.AsArray().Select(tool => tool!).ToList();
        tools.Should().NotBeEmpty();
        tools.Select(tool => tool["name"]!.GetValue<string>()).Should().OnlyHaveUniqueItems().And.OnlyContain(name => Regex.IsMatch(name, "^[a-z][a-z0-9_]+$"));
        tools.Should().OnlyContain(tool => tool["method"]!.GetValue<string>() == "GET", "an Agent has no write tools (ADR 0040)");
        tools.Should().OnlyContain(tool => tool["path"]!.GetValue<string>().StartsWith('/') && tool["description"]!.GetValue<string>().Length > 0);
        tools.SelectMany(tool => tool.AsObject().Select(property => property.Key)).Distinct().Should().BeSubsetOf(["name", "description", "method", "path", "mockResponse"]);
    }

    [Theory]
    [MemberData(nameof(ContractFiles))]
    public void ToolListContract_IsTheFileTheBicepReads_SoTheBicepCannotDriftFromIt(string fileName)
    {
        var bicep = string.Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "infra/estate"), "*.bicep").Select(File.ReadAllText));

        bicep.Should().Contain($"loadJsonContent('../../{ContractFolder}/{fileName}')");
    }

    [Fact]
    public void TheExampleServer_IsAModuleCallWithTheExampleContractsTools()
    {
        var tools = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, ContractFolder, "example.tools.json")))!["tools"]!.AsArray();
        var resources = BicepSnapshot.Resources(Module, [.. Defaults(ToBicep(tools))]);

        resources.Where(resource => Type(resource) == "Microsoft.ApiManagement/service/apis/tools").Should().HaveCount(tools.Count);
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate/agent-mcp-example.bicep")).Should().Contain("tools: contract.tools");
    }

    // The Planner's policy as a deployment would apply it, with the values of its contract: the checks (token, role, rate limit, tool list) hold for it as for the example.
    [Fact]
    public void ThePlannersPolicy_IsFilledWithItsContractsRole_ItsOneTool_AndItsRateLimit()
    {
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, ContractFolder, "planner.tools.json")))!;
        var tools = contract["tools"]!.AsArray();
        var parameters = new[]
        {
            "param apiManagementName = 'apim'", $"param serverName = '{contract["server"]}'", "param purpose = 'Planner.'", $"param tools = {ToBicep(tools)}",
            $"param audience = '{Audience}'", $"param requiredRole = '{contract["role"]}'", "param tenantId = 'tenant-1'", "param rateLimitCalls = 60",
        };
        var resources = BicepSnapshot.Resources(Module, parameters);
        var operationPolicy = Single(resources, "Microsoft.ApiManagement/service/apis/operations/policies")["properties"]!["value"]!.GetValue<string>();
        var inbound = XElement.Parse(Single(resources, "Microsoft.ApiManagement/service/apis/policies")["properties"]!["value"]!.GetValue<string>()).Element("inbound")!;

        inbound.Element("validate-azure-ad-token")!.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().Equal(Audience);
        inbound.Elements("choose").Elements("when").Select(when => when.Attribute("condition")!.Value)
            .Should().Contain(condition => condition.Contains($"\"{contract["role"]}\"", StringComparison.Ordinal))
            .And.Contain(condition => condition.Contains($"\"{tools[0]!["name"]}\".Split('|')", StringComparison.Ordinal));
        inbound.Element("rate-limit-by-key")!.Attribute("calls")!.Value.Should().Be("60");
        operationPolicy.Should().Contain(contract["role"]!.GetValue<string>()).And.Contain(tools[0]!["path"]!.GetValue<string>());
        Regex.IsMatch(operationPolicy, "__[A-Z_]+__").Should().BeFalse();
    }

    private static string Header(XElement scope, string name) => scope.Elements("set-header").Single(header => header.Attribute("name")!.Value == name).Element("value")!.Value;

    private static string[] Defaults(string tools) =>
    [
        "param apiManagementName = 'apim'", "param serverName = 'things'", "param purpose = 'Things.'", $"param tools = {tools}",
        $"param audience = '{Audience}'", $"param requiredRole = '{Role}'", "param tenantId = 'tenant-1'",
    ];

    // A parameter line passed in replaces the default of the same name.
    private static List<JsonNode> Resources(params string[] parameters)
    {
        var tools = ToBicep(new JsonArray(Tools.Select(name => (JsonNode)new JsonObject
        {
            ["name"] = name,
            ["description"] = $"Reads {name}.",
            ["method"] = "GET",
            ["path"] = $"/{name.Replace('_', '-')}",
        }).ToArray()));
        var defaults = Defaults(tools).Where(line => !parameters.Any(given => given.Split(' ')[1] == line.Split(' ')[1]));

        return BicepSnapshot.Resources(Module, [.. defaults, .. parameters]);
    }

    private static string Type(JsonNode resource) => resource["type"]!.GetValue<string>();

    private static JsonNode Single(List<JsonNode> resources, string type) => resources.First(resource => Type(resource) == type);

    // JSON as a Bicep literal: Bicep wants single-quoted strings.
    private static string ToBicep(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray array => "[" + string.Join(", ", array.Select(ToBicep)) + "]",
        JsonObject obj => "{" + string.Join(", ", obj.Select(property => $"{property.Key}: {ToBicep(property.Value)}")) + "}",
        JsonValue value when value.TryGetValue<string>(out var text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'",
        JsonValue value => value.ToJsonString(),
        _ => throw new InvalidOperationException(),
    };
}
