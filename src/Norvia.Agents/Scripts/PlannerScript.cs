using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Planner agent. It reads the assignment options of the Transport Order the question names with its one tool,
/// <c>get_assignment_options</c>, and recommends exactly one option the Assignment rules allow: Own fleet when it fits, otherwise the
/// cheapest Carrier the Carrier Rate Card prices, or Own fleet with its warning named when no Carrier can be priced. It never recommends
/// an option that breaks an Assignment rule, never states a figure the result did not give, and suggests no action beyond a pre-selection: the Planner confirms.
/// </summary>
public static class PlannerScript
{
    public const string GetAssignmentOptions = "get_assignment_options";

    /// <summary>The same read with the what-if tally (#445): the orders the plan has already suggested for Own fleet count against the day.</summary>
    public const string GetAssignmentOptionsAssuming = "get_assignment_options_assuming";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (PlannerQuestions.ParseSuggestAssignmentWithTally(context.Question) is not (var number, var assumed))
        {
            return [new TextContent("{}")];
        }

        if (context.Results.Count > 0)
        {
            return [new TextContent(Answer(number, context.Results[0]))];
        }

        return assumed.Count == 0
            ? [new FunctionCallContent($"call-{context.Turn}", GetAssignmentOptions, new Dictionary<string, object?> { ["transportOrderNumber"] = number })]
            : [new FunctionCallContent(
                $"call-{context.Turn}",
                GetAssignmentOptionsAssuming,
                new Dictionary<string, object?> { ["transportOrderNumber"] = number, ["alreadyAssumed"] = string.Join(',', assumed) })];
    }

    private static string Answer(string number, ScriptedToolResult result)
    {
        var options = Parse(result.Content);
        if (options is null)
        {
            return IntegrationOperationsScript.Json(
                $"The assignment options of Transport Order {number} could not be read, so no option can be recommended.",
                [("The assignment options came back unreadable.", result.Id)],
                "Choose who carries the order yourself; the assistant has no recommendation.",
                "low",
                "needs-a-person");
        }

        var evidence = new List<(string Claim, string Citation)>();
        var own = options.OwnFleet;
        var carriers = options.Carriers.Where(carrier => carrier.MeetsRules && carrier.Cost is not null).OrderBy(carrier => carrier.Cost).ThenBy(carrier => carrier.Name, StringComparer.Ordinal).ToList();
        var cheapest = carriers.FirstOrDefault();
        var warned = own.Fit is "OverCapacity" or "OutsideHorizon";

        if (own.MeetsRules && own.Fit == "Fits")
        {
            evidence.Add(($"Own fleet fits the collection day: {own.Fit}.", result.Id));
            var against = cheapest is null ? string.Empty : $" The cheapest Carrier is {cheapest.Name} at {Money(cheapest.Cost!.Value)}, but Own fleet has the capacity and no extra cost.";
            if (cheapest is not null)
            {
                evidence.Add(($"{cheapest.Name} prices it at {Money(cheapest.Cost!.Value)}.", result.Id));
            }

            return IntegrationOperationsScript.Json(
                $"Own fleet is the best option for Transport Order {options.Number}: it fits the collection day with no warning.",
                evidence,
                $"Assign to Own fleet.{against}",
                "high",
                "assign-own-fleet");
        }

        if (own.MeetsRules && warned && cheapest is null)
        {
            var warning = own.Warning ?? "the collection day is outside the Planning horizon";
            evidence.Add(($"Own fleet has a warning: {warning}.", result.Id));
            evidence.Add(("No Carrier can be priced for this order.", result.Id));
            return IntegrationOperationsScript.Json(
                $"Own fleet is the only option for Transport Order {options.Number}, with a warning, as no Carrier can be priced.",
                evidence,
                $"Assign to Own fleet, accepting this warning: {warning}.",
                "medium",
                "assign-own-fleet");
        }

        if (cheapest is not null)
        {
            evidence.Add(($"{cheapest.Name} prices it at {Money(cheapest.Cost!.Value)}, the lowest Carrier cost.", result.Id));
            var against = own.MeetsRules && warned
                ? $" Own fleet would carry a warning: {own.Warning ?? "the collection day is outside the Planning horizon"}."
                : $" Own fleet cannot take it ({own.Fit}).";
            evidence.Add(($"Own fleet: {own.Fit}.", result.Id));
            return IntegrationOperationsScript.Json(
                $"Carrier {cheapest.Name} is the best option for Transport Order {options.Number}: the cheapest Carrier the Carrier Rate Card prices.",
                evidence,
                $"Assign to Carrier {cheapest.Name} at {Money(cheapest.Cost!.Value)}.{against}",
                "medium",
                cheapest.Id.Length == 0 ? "needs-a-person" : "assign-carrier",
                cheapest.Id.Length == 0 ? null : cheapest.Id);
        }

        var broken = options.Carriers.Select(carrier => carrier.RuleBroken).Prepend(own.RuleBroken).FirstOrDefault(rule => rule is not null);
        evidence.Add((broken is null ? "No option can carry the order." : $"The Assignment rules refuse the order: {broken}.", result.Id));
        return IntegrationOperationsScript.Json(
            $"No option can be recommended for Transport Order {options.Number}.",
            evidence,
            broken is null ? "Choose who carries the order yourself." : $"Resolve the broken rule ({broken}) before assigning.",
            "low",
            "needs-a-person");
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " EUR";

    private static Options? Parse(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ownFleet", out var own) || own.ValueKind != JsonValueKind.Object
                || IntegrationOperationsScript.Text(own, "fit") is not { } fit)
            {
                return null;
            }

            var carriers = root.TryGetProperty("carriers", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object).Select(CarrierOf).ToList()
                : [];
            return new Options(
                IntegrationOperationsScript.Text(root, "transportOrderNumber") ?? "this order",
                new OwnFleet(Meets(own), fit, IntegrationOperationsScript.Text(own, "warning"), IntegrationOperationsScript.Text(own, "ruleBroken")),
                carriers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Carrier CarrierOf(JsonElement entry) => new(
        IntegrationOperationsScript.Text(entry, "carrierId") ?? string.Empty,
        IntegrationOperationsScript.Text(entry, "name") ?? "an unnamed Carrier",
        Meets(entry),
        entry.TryGetProperty("carrierCost", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var amount) ? amount : null,
        IntegrationOperationsScript.Text(entry, "ruleBroken"));

    // An option that does not say it meets the rules is treated as refused: the safe reading.
    private static bool Meets(JsonElement option) => option.TryGetProperty("meetsAssignmentRules", out var meets) && meets.ValueKind == JsonValueKind.True;

    private sealed record Options(string Number, OwnFleet OwnFleet, IReadOnlyList<Carrier> Carriers);

    private sealed record OwnFleet(bool MeetsRules, string Fit, string? Warning, string? RuleBroken);

    private sealed record Carrier(string Id, string Name, bool MeetsRules, decimal? Cost, string? RuleBroken);
}
