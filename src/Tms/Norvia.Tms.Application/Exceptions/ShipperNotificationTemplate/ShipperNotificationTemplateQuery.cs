using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;

/// <summary>The message the Planner's Notify Shipper starts with for the Exception's type (P7), which the Planner can edit.</summary>
public sealed record ShipperNotificationTemplateQuery(string ExceptionNumber) : IQuery<Result<ShipperNotificationTemplateResult>>;
