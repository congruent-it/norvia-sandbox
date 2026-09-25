using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>
/// The callers that may submit Transport Orders for each intake channel, by the client ID in their token (<c>azp</c>,
/// or <c>appid</c> in a v1 token). A caller is registered for one channel only, so it can't choose the channel it
/// submits for. In Azure the intake Logic App's managed identity is the one caller on the Shipper API channel.
/// </summary>
public sealed class IntakeCallersOptions
{
    public const string SectionName = "IntakeCallers";

    public Dictionary<IntakeChannel, string[]> ClientIdsByChannel { get; init; } = [];
}
