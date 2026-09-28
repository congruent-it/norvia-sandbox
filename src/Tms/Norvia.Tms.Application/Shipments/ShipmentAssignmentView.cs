using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>One entry in a Shipment's history of assignments.</summary>
/// <param name="CorrelationId">The Message trail of the assignment or reassignment.</param>
public sealed record ShipmentAssignmentView(CarriedByView CarriedBy, string CorrelationId, DateTimeOffset AssignedAt)
{
    public static ShipmentAssignmentView Of(ShipmentAssignment assignment, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new(CarriedByView.Of(assignment.CarriedBy, carriers), assignment.CorrelationId.Value, assignment.AssignedAt);
    }
}
