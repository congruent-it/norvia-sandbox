using Norvia.Erp.Application.Reset;

namespace Norvia.Erp.Infrastructure.Reset;

/// <summary>
/// For a process that holds the reset handlers but no Service Bus client to peek with, such as the ERP UI. The reset is the ERP API's: the screens
/// have no endpoint for it, and their switch is off, so the handler never gets as far as asking.
/// </summary>
internal sealed class UnavailableQuietCheck : IQuietCheck
{
    public Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This process can't peek the subscriptions; the reset is the ERP API's.");
}
