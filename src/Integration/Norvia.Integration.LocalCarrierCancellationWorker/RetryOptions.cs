using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

public sealed class RetryOptions
{
    /// <summary>How many times a send is retried after the first attempt.</summary>
    [Range(0, 10)]
    public int Count { get; set; } = 3;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(20);
}
