using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft;

/// <summary>
/// Fills a <see cref="BookingDraft"/> from a Shipper's text with the Intake agent: one run, no tools, no retry. The reply is checked against
/// the draft schema before it is returned, and a value without a quote that is in the text is thrown away and flagged as missing, so the agent
/// cannot invent a Shipper reference, a date, a weight or a quantity. The text and the reply are never logged here.
/// </summary>
public sealed class BookingDraftExtractor(IChatClient model, TimeSpan timeout)
{
    private const string NotInText = "The text does not give it.";

    private readonly AgentRunner _runner = new(model, NoTools.Source, new AgentRunnerOptions(timeout));
    private readonly Lazy<Norvia.Agents.Definitions.AgentDefinition> _definition = new(BookingIntakeDefinition.Load);

    public async Task<BookingDraftOutcome> ExtractAsync(string text, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runId = Guid.NewGuid().ToString("N");
        ReplyRunResult run;
        try
        {
            run = await _runner.RunReplyAsync(_definition.Value, new AgentQuestion(BookingIntakeQuestions.Ask(text), correlationId, runId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        JsonNode? reply;
        try
        {
            reply = JsonNode.Parse(run.Reply);
        }
        catch (JsonException)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        if (reply is JsonObject { Count: 1 } refusal && refusal["refusal"]?.GetValue<string>() == BookingDraftFailureCodes.MultipleBookingsInText)
        {
            return Failed(runId, BookingDraftFailureCodes.MultipleBookingsInText);
        }

        if (BookingDraftSchema.Errors(reply).Count > 0)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        return new BookingDraftOutcome(runId, Grounded(reply!.AsObject(), text), null);
    }

    private static BookingDraftOutcome Failed(string runId, string code) => new(runId, null, code);

    // Keeps a value only when its quote is in the text. Everything else is left empty and flagged.
    private static BookingDraft Grounded(JsonObject reply, string text)
    {
        var haystack = Normalize(text);
        var quotes = reply["quotes"]!.AsObject().ToDictionary(quote => quote.Key, quote => quote.Value!.GetValue<string>(), StringComparer.Ordinal);
        var missing = reply["missing"]!.AsArray()
            .Select(field => new MissingField(field!["field"]!.GetValue<string>(), field["reason"]!.GetValue<string>()))
            .ToList();
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        var booking = new JsonObject();

        bool Holds(string path) =>
            quotes.TryGetValue(path, out var quote) && Normalize(quote) is { Length: > 0 } normalized && haystack.Contains(normalized, StringComparison.Ordinal);

        void Drop(string path)
        {
            if (!missing.Exists(field => field.Field == path))
            {
                missing.Add(new MissingField(path, NotInText));
            }
        }

        foreach (var (name, value) in reply["booking"]!.AsObject())
        {
            if (name == "items")
            {
                var items = new JsonArray();
                var index = 0;
                foreach (var item in value!.AsArray())
                {
                    var keptItem = new JsonObject();
                    foreach (var (itemName, itemValue) in item!.AsObject())
                    {
                        var path = $"items.{index}.{itemName}";
                        if (Holds(path))
                        {
                            keptItem[itemName] = itemValue!.DeepClone();
                            kept[path] = quotes[path];
                        }
                        else
                        {
                            Drop(path);
                        }
                    }

                    if (keptItem.Count > 0)
                    {
                        items.Add(keptItem);
                    }

                    index++;
                }

                if (items.Count > 0)
                {
                    booking["items"] = items;
                }
            }
            else if (Holds(name))
            {
                booking[name] = value!.DeepClone();
                kept[name] = quotes[name];
            }
            else
            {
                Drop(name);
            }
        }

        return new BookingDraft(booking, kept, missing);
    }

    // Case and white space (line breaks in an email) do not matter when a quote is looked up.
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
