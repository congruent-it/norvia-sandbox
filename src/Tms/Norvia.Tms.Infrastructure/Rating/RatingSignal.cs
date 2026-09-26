using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.TransportOrders.Rate;

namespace Norvia.Tms.Infrastructure.Rating;

/// <summary>
/// The in-process trigger between an acceptance and the rating worker. Triggers that arrive while one is pending are
/// folded into it: the worker's sweep rates every Transport Order that is Not yet rated, not just one.
/// </summary>
internal sealed class RatingSignal(IOptions<RatingOptions> options) : IRatingTrigger
{
    private readonly Channel<bool> _pending = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    public void Trigger()
    {
        if (options.Value.TriggerOnAcceptance)
        {
            _pending.Writer.TryWrite(true);
        }
    }

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _pending.Reader.ReadAsync(cancellationToken);
}
