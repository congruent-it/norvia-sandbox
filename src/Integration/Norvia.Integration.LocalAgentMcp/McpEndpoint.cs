using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>
/// An Agent's MCP server, played locally (ADR 0041). It makes the checks of the API Management policy in its order
/// (infra/estate/agent-mcp-server/mcp-server.policy.xml): the Agent's token, its read role, the rate limit and the tool list,
/// and then calls the application's agent view as the Agent's identity with the correlation ID, the agent and the tool name.
/// Each refusal is a trace on the Message trail and a JSON body with the correlation ID.
/// </summary>
internal static partial class McpEndpoint
{
    public const string Flow = "agent-mcp-integration-operations";
    public const string CorrelationHeader = "X-Correlation-ID";
    public const string AgentHeader = "X-Agent-ID";
    public const string ToolHeader = "X-Tool-Name";
    public const string BackendClient = "console-agent-api";

    public static IEndpointRouteBuilder MapAgentMcp(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the policy's own checks, in its order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/mcp", HandleAsync).AllowAnonymous();
        // Streamable HTTP lets a server refuse the optional stream; a stateless server has nothing to push.
        endpoints.MapGet("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        endpoints.MapDelete("/mcp", () => Results.Ok()).AllowAnonymous();
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        ToolListContract contract,
        AgentRateLimit rateLimit,
        IOptions<AgentMcpOptions> options,
        TimeProvider time,
        IHttpClientFactory clients,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(rateLimit);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(loggers);

        // The correlation ID of the Agent run travels down as a header (the definition's structured input); a call without one gets a new ID.
        var correlationId = CorrelationIdOf(context.Request);
        context.Response.Headers[CorrelationHeader] = correlationId;
        var logger = loggers.CreateLogger(typeof(McpEndpoint));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = Flow });

        var token = await AgentToken.ValidateAsync(context.Request.Headers.Authorization.ToString(), options.Value);
        if (token is null)
        {
            return Refuse(logger, StatusCodes.Status401Unauthorized, "AgentTokenInvalid", correlationId, "Unauthorized. Access token is missing or invalid.");
        }

        if (!token.Roles.Contains(contract.Role))
        {
            return Refuse(logger, StatusCodes.Status403Forbidden, "AgentRoleMissing", correlationId, "This agent may not use this server.", token.AgentId);
        }

        if (token.AgentId is not { } agentId)
        {
            return Refuse(logger, StatusCodes.Status403Forbidden, "NoAgentIdentity", correlationId, "The token names no calling application.");
        }

        if (rateLimit.Count(agentId) is { } retryAfter)
        {
            context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Refuse(logger, StatusCodes.Status429TooManyRequests, "AgentRateLimited", correlationId, "Too many requests. Try again later.", agentId);
        }

        JsonObject? message;
        try
        {
            message = await JsonSerializer.DeserializeAsync<JsonObject>(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message?["method"]?.GetValue<string>() is not { } method)
        {
            return Results.Json(Failure(null, -32600, "Invalid Request"), statusCode: StatusCodes.Status400BadRequest);
        }

        var id = message["id"]?.DeepClone();
        if (id is null)
        {
            // A notification, such as the client saying it is initialized.
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        return method switch
        {
            "initialize" => Results.Json(Success(id, new JsonObject
            {
                ["protocolVersion"] = message["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "norvia-local-agent-mcp", ["version"] = "1" },
            })),
            "ping" => Results.Json(Success(id, new JsonObject())),
            "tools/list" => Results.Json(Success(id, new JsonObject { ["tools"] = new JsonArray([.. contract.Tools.Select(tool => (JsonNode)McpTools.Describe(tool))]) })),
            "tools/call" => await CallAsync(context, contract, options.Value, time, clients, logger, correlationId, agentId, id, message["params"] as JsonObject),
            _ => Results.Json(Failure(id, -32601, "Method not found")),
        };
    }

    private static async Task<IResult> CallAsync(
        HttpContext context,
        ToolListContract contract,
        AgentMcpOptions options,
        TimeProvider time,
        IHttpClientFactory clients,
        ILogger logger,
        string correlationId,
        string agentId,
        JsonNode id,
        JsonObject? parameters)
    {
        // The name is read from the MCP message, never from a header.
        var name = parameters?["name"]?.GetValue<string>();
        if (name is null || contract.Find(name) is not { } tool)
        {
            return Refuse(logger, StatusCodes.Status403Forbidden, "ToolNotOnList", correlationId, "This agent may not use this tool.", agentId, name);
        }

        using var called = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.AgentId] = agentId, [TrackingKeys.ToolName] = tool.Name });
        if (McpTools.PathOf(tool, parameters?["arguments"] as JsonObject) is not { } path)
        {
            return Results.Json(Failure(id, -32602, $"{tool.Name} needs {string.Join(", ", tool.Parameters)}, each a plain identifier."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.ConsoleAgentApi!.AbsoluteUri.TrimEnd('/') + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.ConsoleTokens, time, agentId, contract.Role));
        request.Headers.Add(CorrelationHeader, correlationId);
        request.Headers.Add(AgentHeader, agentId);
        request.Headers.Add(ToolHeader, tool.Name);

        try
        {
            using var response = await clients.CreateClient(BackendClient).SendAsync(request, context.RequestAborted);
            var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                // A refusal or a failure of the agent view is AgentToolRefused to the Agent, which says it could not read.
                using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "AgentToolRefused"));
                LogToolRefused(logger, tool.Name, (int)response.StatusCode);
                return Results.Json(Success(id, Text($"The agent view answered {(int)response.StatusCode}.", isError: true)));
            }

            using var succeeded = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
            LogToolCalled(logger, tool.Name, agentId);
            return Results.Json(Success(id, Text(body, isError: false)));
        }
        catch (HttpRequestException exception)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "AgentToolRefused"));
            LogToolUnreachable(logger, exception, tool.Name);
            return Results.Json(Success(id, Text("The agent view could not be reached.", isError: true)));
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "AgentToolRefused"));
            LogToolTimedOut(logger, tool.Name);
            return Results.Json(Success(id, Text("The agent view did not answer in time.", isError: true)));
        }
    }

    private static string CorrelationIdOf(HttpRequest request) =>
        request.Headers[CorrelationHeader].ToString() is { Length: > 0 and <= 100 } sent && sent.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? sent
            : Guid.NewGuid().ToString();

    private static JsonObject Text(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject Success(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Failure(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static IResult Refuse(ILogger logger, int status, string failureCode, string correlationId, string text, string? agentId = null, string? toolName = null)
    {
        var keys = TrackingKeys.OutcomeOf(TrackingKeys.Refused, failureCode);
        if (agentId is not null)
        {
            keys[TrackingKeys.AgentId] = agentId;
        }

        if (toolName is not null)
        {
            keys[TrackingKeys.ToolName] = toolName;
        }

        using var scope = logger.BeginScope(keys);
        LogRefused(logger, status, failureCode);
        return Results.Json(new JsonObject { ["messageId"] = correlationId, ["text"] = text }, statusCode: status);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The agent was refused with {StatusCode}: {FailureCode}")]
    private static partial void LogRefused(ILogger logger, int statusCode, string failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {ToolName} called by {AgentId}")]
    private static partial void LogToolCalled(ILogger logger, string toolName, string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {ToolName} was refused by the agent view with {StatusCode}")]
    private static partial void LogToolRefused(ILogger logger, string toolName, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {ToolName}: the agent view could not be reached")]
    private static partial void LogToolUnreachable(ILogger logger, Exception exception, string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {ToolName}: the agent view did not answer in time")]
    private static partial void LogToolTimedOut(ILogger logger, string toolName);
}
