using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.ListOpen;

/// <summary>An Open Exception, in the Planner's list of Open Exceptions (P7).</summary>
/// <param name="CarriedBy">Who carried the Shipment when the Exception opened: the Carrier, or Own fleet.</param>
/// <param name="NumberOfCauses">How many causes it has: the one that opened it and every later one of its type.</param>
/// <param name="AfterDelivered">Whether the Shipment was Delivered already when the Exception opened.</param>
/// <param name="CauseCleared">Whether the cause no longer holds. The Exception stays Open until the Planner resolves it.</param>
/// <param name="Notes">The Planner's notes, in the order they were added.</param>
public sealed record OpenExceptionSummary(
    string ExceptionNumber,
    string ShipmentNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    ExceptionType Type,
    DateTimeOffset OpenedAt,
    int NumberOfCauses,
    bool AfterDelivered,
    bool CauseCleared,
    IReadOnlyList<ExceptionNoteSummary> Notes);
