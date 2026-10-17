using Norvia.DriverApp.Application.Reset;

namespace Norvia.DriverApp.Infrastructure.Reset;

/// <summary>
/// For a process that holds the reset handlers but no Service Bus client to peek with, such as the Driver app's screens. The reset is the Driver app API's: the screens
/// have no endpoint for it, and their switch is off, so the handler never gets as far as asking.
/// </summary>
internal sealed class UnavailableQuietCheck : IQuietCheck
{
    public Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This process can't peek the subscriptions; the reset is the Driver app API's.");
}
