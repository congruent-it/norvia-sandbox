using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The fake model of ADR 0041: it asks for fixed tool calls and gives a fixed answer, so a local run and CI are deterministic
/// and need no Azure. The script is a function of the results so far; it holds no state of its own.
/// </summary>
public sealed class ScriptedChatClient(Func<ScriptContext, IReadOnlyList<AIContent>> script) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var all = messages.ToList();
        IReadOnlyList<AIContent> contents = script(new ScriptContext(all, options));

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents]));
        // A deterministic stand-in for the usage a real model reports: about four characters a token, so a run's budget can be counted locally.
        var input = all.Sum(message => message.Text.Length) / 4;
        var output = response.Text.Length / 4;
        response.Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The scripted model doesn't stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
