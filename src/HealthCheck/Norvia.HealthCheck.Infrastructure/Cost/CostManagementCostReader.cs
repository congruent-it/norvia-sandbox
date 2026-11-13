using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.Cost;

/// <summary>
/// The Azure adapter of <see cref="ICostReader"/>: Cost Management's daily actual cost of the estate's resource group, per resource, read as
/// the app's own managed identity (Cost Management Reader on the resource group), with the Transport Orders per day from the TMS API.
/// Cost Management settles a day about a day late, so yesterday is provisional and today, which is not a whole day, is left out.
/// </summary>
internal sealed class CostManagementCostReader(
    TokenCredential credential,
    IHttpClientFactory clients,
    IOptions<AzureEstateOptions> estate,
    ITransportOrderVolume volume,
    TimeProvider time) : ICostReader
{
    private const string Management = "https://management.azure.com";
    private const string ApiVersion = "2023-11-01";
    private const string Eur = "EUR";

    /// <summary>What a charge Cost Management has no resource for (some marketplace and support charges) is called.</summary>
    internal const string Unassigned = "unassigned";

    public async Task<IReadOnlyList<CostDay>> ReadAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var yesterday = today.AddDays(-1);
        var last = lastDay < yesterday ? lastDay : yesterday;
        if (last < firstDay)
        {
            return [];
        }

        var spendPerDay = await ReadSpendAsync(firstDay, last, cancellationToken);
        var orders = await volume.CountPerDayAsync(firstDay, last, cancellationToken);

        return [.. Enumerable.Range(0, last.DayNumber - firstDay.DayNumber + 1)
            .Select(offset => firstDay.AddDays(offset))
            .Select(date => new CostDay(
                date,
                orders.GetValueOrDefault(date),
                Provisional: date >= yesterday,
                spendPerDay.TryGetValue(date, out var spend) ? spend : []))];
    }

    private async Task<Dictionary<DateOnly, List<ResourceSpend>>> ReadSpendAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(AzureEstateConfiguration.ManagementScopes), cancellationToken);
        var body = new JsonObject
        {
            ["type"] = "ActualCost",
            ["timeframe"] = "Custom",
            ["timePeriod"] = new JsonObject
            {
                ["from"] = $"{firstDay:yyyy-MM-dd}T00:00:00Z",
                ["to"] = $"{lastDay:yyyy-MM-dd}T23:59:59Z",
            },
            ["dataset"] = new JsonObject
            {
                ["granularity"] = "Daily",
                ["aggregation"] = new JsonObject { ["totalCost"] = new JsonObject { ["name"] = "Cost", ["function"] = "Sum" } },
                ["grouping"] = new JsonArray(new JsonObject { ["type"] = "Dimension", ["name"] = "ResourceId" }),
            },
        };

        // Cost Management answers a page at a time and names the next in nextLink; a page not read is spend left out without a word.
        var spendPerDay = new Dictionary<DateOnly, List<ResourceSpend>>();
        string? address = $"{Management}{estate.Value.ResourceGroupId}/providers/Microsoft.CostManagement/query?api-version={ApiVersion}";
        while (address is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await clients.CreateClient(AzureEstateConfiguration.ManagementClient).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var properties = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))?["properties"]
                ?? throw new InvalidOperationException("Cost Management answered with nothing.");
            AddPage(properties, firstDay, lastDay, spendPerDay);
            address = properties["nextLink"]?.GetValue<string>();
        }

        return spendPerDay;
    }

    private static void AddPage(JsonNode properties, DateOnly firstDay, DateOnly lastDay, Dictionary<DateOnly, List<ResourceSpend>> spendPerDay)
    {
        var columns = properties["columns"]!.AsArray().Select(column => column!["name"]!.GetValue<string>()).ToList();
        int Index(string name) => columns.IndexOf(name) is var index and >= 0 ? index : throw new InvalidOperationException($"Cost Management's answer has no {name} column.");
        int cost = Index("Cost"), usageDate = Index("UsageDate"), resourceId = Index("ResourceId"), currency = Index("Currency");

        foreach (var row in properties["rows"]!.AsArray().Select(row => row!.AsArray()))
        {
            var billedIn = row[currency]!.GetValue<string>();
            if (!string.Equals(billedIn, Eur, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The bill is in {billedIn}, not {Eur}; the cost view does not convert currencies.");
            }

            var date = DateOnly.ParseExact(row[usageDate]!.ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
            if (date < firstDay || date > lastDay)
            {
                continue;
            }

            var name = row[resourceId]!.GetValue<string>().Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? Unassigned;
            if (!spendPerDay.TryGetValue(date, out var spend))
            {
                spendPerDay[date] = spend = [];
            }

            spend.Add(new ResourceSpend(name, row[cost]!.GetValue<decimal>()));
        }
    }
}
