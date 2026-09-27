using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Shipments.GetByNumber;

public sealed record GetShipmentQuery(string ShipmentNumber) : IQuery<Result<ShipmentView>>;
