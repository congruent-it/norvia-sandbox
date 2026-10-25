using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>
/// The local adapter of <see cref="IEstateConfiguration"/>: it reads a JSON snapshot kept in the repo, so a local health check finds
/// what the snapshot says and not what a running estate is. In Azure <see cref="AzureEstateConfiguration"/> produces the
/// same shape. The snapshots follow the as-found presets in <c>infra/presets</c>, and a test keeps them so.
/// </summary>
internal sealed class SnapshotEstateConfiguration(IOptions<SnapshotOptions> options) : IEstateConfiguration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Load(options.Value.Snapshot));

    public static EstateConfiguration Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"snapshots/{name}.snapshot.json")
            ?? throw new InvalidOperationException($"There is no snapshot named '{name}'.");
        var file = JsonSerializer.Deserialize<File>(stream, Json) ?? throw new InvalidOperationException($"The snapshot '{name}' is empty.");
        return new EstateConfiguration(
            file.Name ?? name,
            [.. (file.ServiceBusEntities ?? []).Select(entity => new ServiceBusEntity(entity.Name ?? throw new InvalidOperationException($"A Service Bus entity of the snapshot '{name}' has no name."), entity.HasDeadLetterAlert))]);
    }

    // The file's own shape, so a missing name is told apart from a default.
    private sealed class File
    {
        public string? Name { get; set; }

        public List<FileEntity>? ServiceBusEntities { get; set; }
    }

    private sealed class FileEntity
    {
        public string? Name { get; set; }

        public bool HasDeadLetterAlert { get; set; }
    }
}
