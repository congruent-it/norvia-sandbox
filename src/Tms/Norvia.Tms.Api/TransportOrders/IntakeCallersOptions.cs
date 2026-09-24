using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>
/// The callers that may submit Transport Orders, each for its own intake channel, keyed by the client ID in their token
/// (<c>azp</c>, or <c>appid</c> in a v1 token). In Azure the intake Logic App's managed identity is the one caller on
/// the Shipper API channel, so a caller can't choose the channel it submits for.
/// </summary>
public sealed class IntakeCallersOptions
{
    public const string SectionName = "IntakeCallers";

    public Dictionary<string, IntakeChannel> ChannelByClientId { get; init; } = [];
}
