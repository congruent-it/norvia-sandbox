using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing.Release;

/// <summary>
/// Whether a Transport Order may be released without a Proof of delivery. The release rule itself is the order's own
/// (<see cref="TransportOrder.ReleaseRefusal"/>, which the release command applies too); this adds the other conditions that would
/// still keep the order from being Billable, so Customer service, and the Customer service agent, hear about them before a release.
/// </summary>
public sealed class ReleaseEligibilityCheck(
    BillingCheck billing,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ExceptionCase> exceptions)
{
    public async Task<ReleaseEligibility> CheckAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var shipment = await billing.FindShipmentAsync(order, cancellationToken);
        var deliveredAt = shipment is null ? null : await billing.DeliveredAtAsync(shipment, cancellationToken);
        var reasons = new List<string>();
        if (order.ReleaseRefusal(deliveredAt) is { } refusal)
        {
            reasons.Add(refusal.Code.ToString());
        }

        if (shipment is not null && deliveredAt is not null)
        {
            if (await proofsOfDelivery.AnyAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken))
            {
                reasons.Add(ReleaseEligibility.ProofOfDeliveryReceived);
            }

            if (order.RatingResult != RatingResult.Rated)
            {
                reasons.Add(ReleaseEligibility.NotRated);
            }

            if (await exceptions.AnyAsync(new OpenExceptionOfShipmentSpec(shipment.Id), cancellationToken))
            {
                reasons.Add(ReleaseEligibility.OpenException);
            }
        }

        return new ReleaseEligibility(order.Number!.Value, reasons.Count == 0, shipment?.Number.Value, deliveredAt, reasons);
    }
}
