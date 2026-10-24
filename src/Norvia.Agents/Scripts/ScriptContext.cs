using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>What a script sees when the runner asks the fake model for its next turn: the run's inputs and the tool results so far.</summary>
public sealed partial class ScriptContext
{
    private readonly ChatOptions? _options;

    internal ScriptContext(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        _options = options;
        var all = messages.ToList();
        Question = all.FirstOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;
        Turn = all.Count(message => message.Role == ChatRole.Assistant);
        Results =
        [
            .. all.SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(result => Delimited().Match(result.Result?.ToString() ?? string.Empty))
                .Where(match => match.Success)
                .Select(match => new ScriptedToolResult(match.Groups["id"].Value, match.Groups["tool"].Value, match.Groups["content"].Value)),
        ];
    }

    /// <summary>The question the run was started with.</summary>
    public string Question { get; }

    /// <summary>How many times the model has answered so far; a call's ID is made of it.</summary>
    public int Turn { get; }

    /// <summary>The results the model has been given, in order.</summary>
    public IReadOnlyList<ScriptedToolResult> Results { get; }

    public string Input(string name) =>
        _options?.AdditionalProperties?.TryGetValue(name, out var value) == true ? value?.ToString() ?? string.Empty : string.Empty;

    [GeneratedRegex("^<tool_result id=\"(?<id>[^\"]+)\" tool=\"(?<tool>[^\"]+)\">\n(?<content>.*)\n</tool_result>$", RegexOptions.Singleline)]
    private static partial Regex Delimited();
}

/// <summary>A tool result as the model read it, without its delimiters.</summary>
public sealed record ScriptedToolResult(string Id, string Tool, string Content);
