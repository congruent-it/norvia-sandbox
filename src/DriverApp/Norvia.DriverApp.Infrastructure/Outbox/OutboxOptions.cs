using System.ComponentModel.DataAnnotations;

namespace Norvia.DriverApp.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(typeof(TimeSpan), "00:00:00.050", "00:01:00")]
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;
}
