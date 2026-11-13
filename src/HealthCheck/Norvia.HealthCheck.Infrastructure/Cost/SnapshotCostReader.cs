using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Cost;

namespace Norvia.HealthCheck.Infrastructure.Cost;

/// <summary>
/// The local adapter of <see cref="ICostReader"/>: it reads a modelled cost snapshot kept in the repo, in the shape the Azure adapter
/// produces. Locally there is no Cost Management and so no lag: no day is provisional.
/// </summary>
internal sealed class SnapshotCostReader(IOptions<CostSnapshotOptions> options) : ICostReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<CostDay>> ReadAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        IReadOnlyList<CostDay> days = [.. Days(options.Value.CostSnapshot).Where(day => day.Date >= firstDay && day.Date <= lastDay)];
        return Task.FromResult(days);
    }

    /// <summary>Every day of a named cost snapshot, oldest first. None is provisional: locally there is no lag.</summary>
    internal static IReadOnlyList<CostDay> Days(string name) =>
        [.. Load(name).Days
            .OrderBy(day => day.Date)
            .Select(day => new CostDay(
                day.Date,
                day.TransportOrders,
                Provisional: false,
                [.. day.SpendByResource.Select(spend => new ResourceSpend(spend.Key, spend.Value))]))];

    private static CostSnapshot Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"snapshots/{name}.cost-snapshot.json")
            ?? throw new InvalidOperationException($"There is no cost snapshot named '{name}'.");
        return JsonSerializer.Deserialize<CostSnapshot>(stream, Json) ?? throw new InvalidOperationException($"The cost snapshot '{name}' is empty.");
    }

    private sealed record CostSnapshot(List<SnapshotDay> Days);

    private sealed record SnapshotDay(DateOnly Date, int TransportOrders, Dictionary<string, decimal> SpendByResource);
}
