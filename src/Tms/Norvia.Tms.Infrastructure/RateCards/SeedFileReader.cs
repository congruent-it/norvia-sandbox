using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>Reads one part of the seed file, which plays the ERP until its sync exists.</summary>
internal static class SeedFileReader
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    public static TSeed Read<TSeed>(IOptions<SeedOptions> options)
    {
        using var file = File.OpenRead(options.Value.Path);
        return JsonSerializer.Deserialize<TSeed>(file, SeedJson)
            ?? throw new InvalidDataException($"The seed file '{options.Value.Path}' is empty.");
    }
}
