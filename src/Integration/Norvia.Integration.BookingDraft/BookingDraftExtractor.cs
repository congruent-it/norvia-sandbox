using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft;

/// <summary>
/// Fills a <see cref="BookingDraft"/> from a Shipper's text with the Intake agent: one run, no tools, no retry. The reply is checked against
/// the draft schema before it is returned, and a value without a quote that is in the text is thrown away and flagged as missing, so the agent
/// cannot invent a Shipper reference, a date, a weight or a quantity. The text and the reply are never logged here.
/// </summary>
public sealed class BookingDraftExtractor(IAgentClient agent)
{
    private const string NotInText = "The text does not give it.";

    /// <summary>The extractor over the in-process runner, which is how it runs locally and in the tests.</summary>
    public BookingDraftExtractor(IChatClient model, TimeSpan timeout)
        : this(new InProcessAgentClient(model, timeout))
    {
    }

    public async Task<BookingDraftOutcome> ExtractAsync(string text, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runId = Guid.NewGuid().ToString("N");
        string reply;
        try
        {
            reply = JsonReplies.WithoutFence(await agent.AskAsync(new AgentQuestion(BookingIntakeQuestions.Ask(text), correlationId, runId), cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(reply);
        }
        catch (JsonException)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        if (parsed is JsonObject { Count: 1 } refusal && refusal["refusal"]?.GetValue<string>() == BookingDraftFailureCodes.MultipleBookingsInText)
        {
            return Failed(runId, BookingDraftFailureCodes.MultipleBookingsInText);
        }

        if (BookingDraftSchema.Errors(parsed).Count > 0)
        {
            return Failed(runId, BookingDraftFailureCodes.AgentUnavailable);
        }

        return new BookingDraftOutcome(runId, Grounded(parsed!.AsObject(), text), null);
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

        // The quote is in the text, and every number of the value is in the quote: "5 t" cannot hide behind the quote "2 pallets".
        bool Holds(string path, JsonNode? value) =>
            quotes.TryGetValue(path, out var quote)
            && Normalize(quote) is { Length: > 0 } normalized
            && haystack.Contains(normalized, StringComparison.Ordinal)
            && NumbersOfValue(value, path).All(NumbersIn(quote).Contains);

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
                        if (Holds(path, itemValue))
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
            else if (Holds(name, value))
            {
                booking[name] = value!.DeepClone();
                kept[name] = quotes[name];
            }
            else
            {
                Drop(name);
            }
        }

        return new BookingDraft(booking, kept, missing, Ignored(reply, haystack));
    }

    // What the agent says it left out, as far as the text holds it: a snippet that is not in the text is not shown as if it were.
    private static List<string> Ignored(JsonObject reply, string haystack) =>
        [.. (reply["ignored"] as JsonArray ?? [])
            .Select(snippet => snippet!.GetValue<string>().Trim())
            .Where(snippet => snippet.Length > 0 && haystack.Contains(Normalize(snippet), StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];

    private static readonly string[] DateFields = ["pickupFrom", "pickupUntil", "deliverFrom", "deliverUntil"];

    // The numbers a value has to find in its quote. A date leaves out its month (dd-MM-yyyy HH:mm), which a text may write as a name
    // ("14 November 2030"): the day, the year and the time still have to be there.
    private static List<string> NumbersOfValue(JsonNode? value, string path)
    {
        var numbers = NumbersIn(value?.ToString() ?? string.Empty);
        if (DateFields.Contains(path, StringComparer.Ordinal) && numbers.Count == 5)
        {
            numbers.RemoveAt(1);
        }

        return numbers;
    }

    // Numbers without leading zeros, so "08:00" and "8:00" agree.
    private static List<string> NumbersIn(string text) =>
        [.. Regex.Matches(text, "[0-9]+").Select(match => match.Value.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0")];

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
