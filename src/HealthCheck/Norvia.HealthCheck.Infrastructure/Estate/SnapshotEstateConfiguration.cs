using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>
/// The local adapter of <see cref="IEstateConfiguration"/>: it reads a JSON snapshot kept in the repo, so a local health check finds
/// what the snapshot says and not what a running estate is. In Azure <see cref="AzureEstateConfiguration"/> produces the
/// same shape. There is a snapshot for the fixed estate and one for each preset in <c>infra/presets</c>, and a test keeps each so.
/// </summary>
internal sealed class SnapshotEstateConfiguration(IOptions<SnapshotOptions> options) : IEstateConfiguration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        progress?.Report($"Reading the {options.Value.Snapshot} snapshot");
        return Task.FromResult(Load(options.Value.Snapshot));
    }

    public static EstateConfiguration Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"snapshots/{name}.snapshot.json")
            ?? throw new InvalidOperationException($"There is no snapshot named '{name}'.");
        var estate = JsonSerializer.Deserialize<EstateConfiguration>(stream, Json) ?? throw new InvalidOperationException($"The snapshot '{name}' is empty.");
        if (estate.ServiceBusEntities.Any(entity => string.IsNullOrWhiteSpace(entity.Name)) || estate.Flows.Any(flow => string.IsNullOrWhiteSpace(flow.Name)))
        {
            throw new InvalidOperationException($"An entity or flow of the snapshot '{name}' has no name.");
        }

        return estate;
    }
}
