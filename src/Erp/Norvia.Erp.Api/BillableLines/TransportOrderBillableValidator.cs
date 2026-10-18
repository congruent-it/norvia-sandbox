using System.Text.RegularExpressions;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Api.BillableLines;

/// <summary>
/// Checks a <c>TransportOrderBillable</c> against the Canonical Model's own schema rules, at the boundary (coding guidelines §6), and maps
/// it to what the domain takes. The billing rules (the Shipper exists, the charge lines add up) are the domain's.
/// </summary>
internal static partial class TransportOrderBillableValidator
{
    public static (BillableLineData? Line, Dictionary<string, string[]> Errors) Validate(TransportOrderBillableRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors[string.Empty] = ["The body must be a TransportOrderBillable."];
            return (null, errors);
        }

        Require(errors, request.EventType == "TransportOrderBillable", nameof(request.EventType), "The eventType must be TransportOrderBillable.");
        Require(errors, request.EventVersion == 1, nameof(request.EventVersion), "The eventVersion must be 1.");
        Require(errors, !string.IsNullOrWhiteSpace(request.CorrelationId), nameof(request.CorrelationId), "The correlationId is required.");
        Require(errors, !string.IsNullOrWhiteSpace(request.IntakeCorrelationId), nameof(request.IntakeCorrelationId), "The intakeCorrelationId is required.");
        Require(errors, TransportOrderNumber().IsMatch(request.TransportOrderNumber), nameof(request.TransportOrderNumber), "The transportOrderNumber must look like TO-0000123.");
        Require(errors, !string.IsNullOrWhiteSpace(request.ShipperId), nameof(request.ShipperId), "The shipperId is required.");
        Require(errors, !string.IsNullOrWhiteSpace(request.ShipperReference), nameof(request.ShipperReference), "The shipperReference is required.");
        Require(errors, ShipmentNumber().IsMatch(request.ShipmentNumber), nameof(request.ShipmentNumber), "The shipmentNumber must look like SH-0000123.");

        // The deserializer refuses a missing or null member, so the Freight Charge and its lines are there.
        var charge = request.FreightCharge;
        Require(errors, charge.Currency == FreightCharge.Currency, "FreightCharge.Currency", "The currency must be EUR.");
        Require(errors, charge.Total >= 0 && HasCentsOnly(charge.Total), "FreightCharge.Total", "The total must be a non-negative amount in cents.");
        Require(
            errors,
            charge.ChargeLines.All(line => line.Amount >= 0 && HasCentsOnly(line.Amount)),
            "FreightCharge.ChargeLines",
            "Each charge line needs a non-negative amount in cents.");
        Require(
            errors,
            charge.RateCardVersion is { Version: >= 1 } version && !string.IsNullOrWhiteSpace(version.RateCardId),
            "FreightCharge.RateCardVersion",
            "The rateCardVersion needs a rateCardId and a version of 1 or more.");

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var freightCharge = new FreightCharge(
            charge.Total,
            [.. charge.ChargeLines.Select(line => new ChargeLine(line.Type, line.Amount))],
            charge.RateCardVersion.RateCardId,
            charge.RateCardVersion.Version);

        return (
            new BillableLineData(
                request.TransportOrderNumber,
                new ShipperId(request.ShipperId),
                request.ShipperReference,
                request.ShipmentNumber,
                request.DeliveryTime,
                freightCharge,
                request.ReleasedWithoutProofOfDelivery,
                request.IntakeCorrelationId,
                request.CorrelationId),
            errors);
    }

    private static bool HasCentsOnly(decimal amount) => decimal.Round(amount, 2) == amount;

    private static void Require(Dictionary<string, string[]> errors, bool condition, string field, string message)
    {
        if (!condition)
        {
            errors[field] = [message];
        }
    }

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex TransportOrderNumber();

    [GeneratedRegex("^SH-[0-9]{7}$")]
    private static partial Regex ShipmentNumber();
}
