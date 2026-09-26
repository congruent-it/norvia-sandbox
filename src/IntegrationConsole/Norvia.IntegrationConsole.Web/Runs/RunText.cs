using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.Runs;

/// <summary>How a run and its steps read on the page.</summary>
public static class RunText
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        // As it was sent: a Consignee's name keeps its accents, and markup its angle brackets. Blazor escapes the page.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What run history keeps and for how long, once it no longer keeps a run.</summary>
    public static string NoLongerKept(int runHistoryDays) => $"Body no longer kept (run history is kept for {runHistoryDays} days).";

    /// <summary>A body in full: indented when it's JSON, as it is otherwise.</summary>
    public static string Body(string content)
    {
        try
        {
            return JsonNode.Parse(content) is { } json ? json.ToJsonString(Indented) : content;
        }
        catch (JsonException)
        {
            return content;
        }
    }

    /// <summary>When a step started, or a dash when it never ran.</summary>
    public static string Started(DateTimeOffset? start) => start is { } moment ? ConsoleTime.Format(moment) : "–";

    /// <summary>"ServiceUnavailable: The TMS answered 503.", or nothing when the step ended without an error.</summary>
    public static string? Of(StepError? error) => error switch
    {
        null => null,
        { Message: { Length: > 0 } message } => $"{error.Code}: {message}",
        _ => error.Code,
    };
}
