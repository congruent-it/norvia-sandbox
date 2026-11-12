using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Shipper tracking assistant (ADR 0041, ADR 0049): it restates the order's Tracking status, ETA and last update, in
/// the language of the question, from the tracking data in its input and nothing else. What the data lacks, and every question about a price, a
/// cause, a guarantee or an instruction, it answers with no information. It makes no tool call and cites the field behind each claim.
/// </summary>
public static class ShipperTrackingAssistantScript
{
    private static readonly string[] NoInformationWords =
    [
        "price", "prijs", "cost", "kost", "why", "waarom", "reason", "reden", "guarantee", "garantie", "promise", "beloof", "how much", "hoeveel",
        "compensation", "vergoeding", "ignore", "negeer",
    ];

    private static readonly string[] DutchWords = ["waar", "mijn", "bestelling", "zending", "wanneer", "wordt", "geleverd", "het", "de", "een", "ik", "wat", "hoe", "niet", "nog", "komt", "aan", "levering", "kost", "prijs", "waarom"];

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context) => [new TextContent(Reply(context.Question).ToJsonString())];

    private static JsonObject Reply(string input)
    {
        var question = Between(input, "<question>", "</question>").Trim().Split('\n')[^1];
        var data = JsonNode.Parse(Between(input, "<tracking_data>", "</tracking_data>"));
        var dutch = question.Split([' ', ',', '?', '.', '!'], StringSplitOptions.RemoveEmptyEntries).Count(word => DutchWords.Contains(word.ToLowerInvariant())) >= 2;

        if (NoInformationWords.Any(word => question.Contains(word, StringComparison.OrdinalIgnoreCase)) || Subject(data) is not var (path, order))
        {
            return Result("I have no information about that.", [], noInformation: true);
        }

        var claims = new List<string> { $"{path}.status" };
        var status = StatusLabel(order["status"]?.GetValue<string>() ?? string.Empty, dutch);
        var number = order["transportOrderNumber"]!.GetValue<string>();
        var text = $"{number} is {status}.";

        var eta = order["eta"]?.GetValue<string>();
        var lastUpdate = order["lastUpdate"]?.GetValue<string>();
        if (eta is not null)
        {
            claims.Add($"{path}.eta");
            text += dutch ? $" De ETA is een schatting: {eta}" : $" The ETA is an estimate: {eta}";
            if (lastUpdate is not null)
            {
                claims.Add($"{path}.lastUpdate");
                text += dutch ? $" (laatst bijgewerkt {lastUpdate})" : $" (last updated {lastUpdate})";
            }

            text += ".";
        }
        else
        {
            text += dutch ? " Er is nog geen ETA." : " There is no ETA yet.";
            var from = order["deliverFrom"]?.GetValue<string>();
            var until = order["deliverUntil"]?.GetValue<string>();
            if (from is not null && until is not null)
            {
                claims.Add($"{path}.deliverFrom");
                claims.Add($"{path}.deliverUntil");
                text += dutch ? $" Het afleverwindow is {from} tot {until}." : $" The delivery window is {from} to {until}.";
            }
        }

        return Result(text, claims, noInformation: false);
    }

    // The order the answer is about: the open one, the one the question named, or the only one in the list.
    private static (string Path, JsonNode Order)? Subject(JsonNode? data)
    {
        if (data?["detail"] is { } detail)
        {
            return ("detail", detail);
        }

        if (data?["found"] is { } found)
        {
            return ("found", found);
        }

        return data?["orders"]?.AsArray() is { Count: 1 } orders ? ("orders[0]", orders[0]!) : null;
    }

    private static string StatusLabel(string status, bool dutch) => status switch
    {
        "Received" => dutch ? "Ontvangen" : "Received",
        "Scheduled" => dutch ? "Ingepland" : "Scheduled",
        "Collected" => dutch ? "Opgehaald" : "Collected",
        "InTransit" => dutch ? "Onderweg" : "In transit",
        "Delivered" => dutch ? "Afgeleverd" : "Delivered",
        _ => status,
    };

    private static JsonObject Result(string answer, IEnumerable<string> fields, bool noInformation) => new()
    {
        ["answer"] = answer,
        ["claims"] = new JsonArray([.. fields.Select(field => (JsonNode)new JsonObject { ["field"] = field })]),
        ["noInformation"] = noInformation,
    };

    private static string Between(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = text.LastIndexOf(close, StringComparison.Ordinal);
        return start < 0 || end < start ? string.Empty : text[(start + open.Length)..end].Trim();
    }
}
