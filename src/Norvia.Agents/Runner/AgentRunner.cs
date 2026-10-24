using Microsoft.Extensions.AI;

namespace Norvia.Agents.Runner;

/// <summary>
/// The Foundry agent loop, run in process (ADR 0041): the model asks for tools, the runner calls them and gives the model the
/// delimited results, until the model answers with a <see cref="Suggestion"/>. It exposes only the tools the definition lists,
/// refuses a call to any other, and caps the calls per question.
/// </summary>
public sealed class AgentRunner(IChatClient model, IToolSource toolSource, AgentRunnerOptions options)
{
    public async Task<AgentRunResult> RunAsync(Definitions.AgentDefinition definition, AgentQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(question);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        try
        {
            return await LoopAsync(definition, question, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AgentRunException($"The Agent run took longer than {options.Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception exception) when (exception is not (AgentRunException or OperationCanceledException))
        {
            throw new AgentRunException("The Agent run failed.", exception);
        }
    }

    private async Task<AgentRunResult> LoopAsync(Definitions.AgentDefinition definition, AgentQuestion question, CancellationToken cancellationToken)
    {
        await using var session = await toolSource.OpenAsync(new ToolContext(question.CorrelationId, definition.Inputs.CorrelationIdHeader), cancellationToken);
        var listed = session.Tools.Where(tool => definition.Tools.Names.Contains(tool.Name, StringComparer.Ordinal)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        var chat = new ChatOptions
        {
            Temperature = (float)definition.Model.Temperature,
            MaxOutputTokens = definition.Model.MaxOutputTokens,
            Tools = [.. listed.Values],
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [RunProperties.CorrelationId] = question.CorrelationId,
                [RunProperties.RunId] = question.RunId,
            },
        };
        List<ChatMessage> messages = [new(ChatRole.System, definition.Instructions), new(ChatRole.User, question.Text)];
        var calls = new List<ToolCallRecord>();

        while (true)
        {
            var response = await model.GetResponseAsync(messages, chat, cancellationToken);
            messages.AddRange(response.Messages);

            var requested = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToList();
            if (requested.Count == 0)
            {
                return new AgentRunResult(
                    question.RunId,
                    SuggestionParser.Parse(response.Text, calls.ToDictionary(call => call.ResultId, call => call.ToolName, StringComparer.Ordinal)),
                    calls);
            }

            var results = new List<AIContent>();
            foreach (var call in requested)
            {
                if (!listed.TryGetValue(call.Name, out var tool))
                {
                    throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The model asked for '{call.Name}', which the definition doesn't list.");
                }

                if (calls.Count >= definition.Model.MaxToolCalls)
                {
                    throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The model asked for more than {definition.Model.MaxToolCalls} tool calls.");
                }

                var resultId = $"r{calls.Count + 1}";
                calls.Add(new ToolCallRecord(resultId, tool.Name));
                results.Add(new FunctionResultContent(call.CallId, ToolResults.Delimit(resultId, tool.Name, await InvokeAsync(tool, call, cancellationToken))));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));
        }
    }

    private static async Task<string> InvokeAsync(AIFunction tool, FunctionCallContent call, CancellationToken cancellationToken)
    {
        object? raw;
        try
        {
            raw = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The tool '{tool.Name}' was refused.", exception);
        }

        var text = ToolResults.TextOf(raw, out var isError);
        return isError ? throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The tool '{tool.Name}' was refused.") : text;
    }
}

/// <summary>The keys the runner puts in <see cref="ChatOptions.AdditionalProperties"/>, for a model client that wants them (the scripted fake does).</summary>
public static class RunProperties
{
    public const string CorrelationId = "correlationId";
    public const string RunId = "runId";
}
