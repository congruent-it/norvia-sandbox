using Norvia.Tms.Application.Reset;

namespace Norvia.Tms.Infrastructure.Reset;

/// <summary>
/// For a process that holds the reset handlers but no Service Bus client to peek with, such as the TMS UI. The reset is the TMS API's: the UI has no endpoint for it,
/// and its switch is off, so the handler never gets as far as asking.
/// </summary>
internal sealed class UnavailableQuietCheck : IQuietCheck
{
    public Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This process can't peek the subscriptions; the reset is the TMS API's.");
}
