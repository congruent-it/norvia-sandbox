using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ConsigneeNotifications;

/// <summary>What the Consignee notification Logic App needs to send the email for a Transport Order (#983).</summary>
public sealed record GetConsigneeNotificationQuery(string TransportOrderNumber) : IQuery<Result<ConsigneeNotificationContent>>;

/// <summary>
/// Where to send the email and what link it carries. Both are secrets: the address is personal data and the tracking code is a credential
/// (ADR 0052), so <see cref="ToString"/> prints neither and a log line that holds this record can't leak them.
/// </summary>
public sealed record ConsigneeNotificationContent(string TransportOrderNumber, string Email, string TrackingCode)
{
    public override string ToString() => nameof(ConsigneeNotificationContent);
}
