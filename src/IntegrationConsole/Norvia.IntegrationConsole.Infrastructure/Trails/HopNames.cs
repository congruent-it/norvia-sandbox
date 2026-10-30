using System.Text.RegularExpressions;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// What a hop is called on its trail, after its application and what happened there. Both trail sources name hops
/// alike, so a trail reads the same locally as in Azure: an application by the name it has in the AppHost or the estate,
/// such as <c>tms-api</c> or <c>logic-intake</c>, without what Azure adds to it (<see cref="ApplicationOf"/>).
/// </summary>
internal static partial class HopNames
{
    private static readonly HashSet<string> Methods =
        new(["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE", "CONNECT"], StringComparer.Ordinal);

    /// <summary>
    /// A call, by its method and the path actually called, such as <c>tms-api: POST /v1/transport-orders/TO-0000042/assign</c>.
    /// ASP.NET Core names a request after its route template, placeholders and all, to keep span names low in
    /// cardinality; the console shows one trail at a time, so the business key in the path is what it wants. A call with
    /// no path, or whose method is neither recorded nor at the start of its name, such as API Management's operation, keeps
    /// its name.
    /// </summary>
    public static string OfCall(string application, string name, string? method, string? path)
    {
        var called = method is { Length: > 0 } ? method : name.Split(' ', 2)[0];
        return path is { Length: > 0 } && Methods.Contains(called)
            ? $"{ApplicationOf(application)}: {called} {path}"
            : $"{ApplicationOf(application)}: {name}";
    }

    /// <summary>
    /// An MCP message API Management served, by its MCP method and, for a <c>tools/call</c>, the tool, such as <c>apim-norvia: MCP initialize</c>
    /// or <c>apim-norvia: MCP tools/call run_checks</c>. Every message is a POST to the server's one path, so the path says nothing of what it did.
    /// </summary>
    public static string OfMcp(string application, string mcpMethod, string? tool) =>
        tool is { Length: > 0 } ? $"{ApplicationOf(application)}: MCP {mcpMethod} {tool}" : $"{ApplicationOf(application)}: MCP {mcpMethod}";

    /// <summary>The GET that opens an MCP server's event stream, which carries no message of its own and so logs no method.</summary>
    public static string OfMcpStream(string application) => $"{ApplicationOf(application)}: MCP event stream";

    /// <summary>Whether a call is the GET on an MCP server's path (<c>/agents/&lt;server&gt;/mcp</c>) that opens its event stream.</summary>
    public static bool IsMcpStream(string? name, string? path) =>
        name is not null && name.StartsWith("GET ", StringComparison.Ordinal) && path is not null && McpPath().IsMatch(path);

    [GeneratedRegex("^/agents/[^/]+/mcp$")]
    private static partial Regex McpPath();

    /// <summary>What an application did outside any call, by the span it started for it, such as <c>tms-api: Rate Transport Order</c>.</summary>
    public static string OfSpan(string application, string name) => $"{ApplicationOf(application)}: {name}";

    /// <summary>A Logic App run, by its workflow, such as <c>logic-intake: transport-order-intake</c>.</summary>
    public static string OfRun(string logicApp, string workflow) => $"{ApplicationOf(logicApp)}: {workflow}";

    /// <summary>
    /// What an application recorded outside any call: the event it published, such as
    /// <c>tms-api: publish ShipmentPlanned</c>, when its records say which, or else only the application.
    /// </summary>
    public static string OfRecords(string application, string? eventType) =>
        eventType is { Length: > 0 } ? $"{ApplicationOf(application)}: publish {eventType}" : ApplicationOf(application);

    /// <summary>
    /// An application's name without what Azure adds to it in Application Insights: a container app's environment,
    /// which Container Apps' OpenTelemetry agent gives it as <c>service.namespace</c> (<c>[appseowsgwet24y3e]/tms-api</c>);
    /// API Management's region (<c>apim-norvia-eowsgwet24y3e West Europe</c>); and the token the estate ends a resource's
    /// name with, <c>uniqueString(resourceGroup().id)</c>, which is always 13 characters (<c>logic-intake-eowsgwet24y3e</c>).
    /// </summary>
    private static string ApplicationOf(string application)
    {
        var name = EnvironmentPrefix().Replace(application, string.Empty);
        name = name.Split(' ', 2)[0];
        return ResourceToken().Replace(name, string.Empty);
    }

    [GeneratedRegex(@"^\[[^\]]*\]/")]
    private static partial Regex EnvironmentPrefix();

    [GeneratedRegex("-[a-z0-9]{13}$")]
    private static partial Regex ResourceToken();
}
