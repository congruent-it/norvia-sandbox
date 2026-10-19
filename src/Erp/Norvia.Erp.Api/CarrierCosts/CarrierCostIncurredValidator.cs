using System.Text.RegularExpressions;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Api.CarrierCosts;

/// <summary>
/// Checks a <c>CarrierCostIncurred</c> against the Canonical Model's own schema rules, at the boundary (coding guidelines §6), and maps
/// it to what the domain takes. The Carrier cost rules (the Carrier exists, the charge lines add up) are the domain's.
/// </summary>
internal static partial class CarrierCostIncurredValidator
{
    private const int MaxRemarkLength = 500;

    public static (CarrierCostData? Cost, Dictionary<string, string[]> Errors) Validate(CarrierCostIncurredRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors[string.Empty] = ["The body must be a CarrierCostIncurred."];
            return (null, errors);
        }

        Require(errors, request.EventType == "CarrierCostIncurred", nameof(request.EventType), "The eventType must be CarrierCostIncurred.");
        Require(errors, request.EventVersion == 1, nameof(request.EventVersion), "The eventVersion must be 1.");
        Require(errors, !string.IsNullOrWhiteSpace(request.CorrelationId), nameof(request.CorrelationId), "The correlationId is required.");
        Require(errors, CarrierBookingNumber().IsMatch(request.CarrierBookingNumber), nameof(request.CarrierBookingNumber), "The carrierBookingNumber must look like CB-0000123.");
        Require(errors, !string.IsNullOrWhiteSpace(request.CarrierBookingReference), nameof(request.CarrierBookingReference), "The carrierBookingReference is required.");
        Require(errors, ShipmentNumber().IsMatch(request.ShipmentNumber), nameof(request.ShipmentNumber), "The shipmentNumber must look like SH-0000123.");
        Require(errors, TransportOrderNumber().IsMatch(request.TransportOrderNumber), nameof(request.TransportOrderNumber), "The transportOrderNumber must look like TO-0000123.");
        Require(errors, !string.IsNullOrWhiteSpace(request.CarrierId), nameof(request.CarrierId), "The carrierId is required.");

        // The deserializer refuses a missing or null member, so the Carrier cost and its lines are there.
        var cost = request.CarrierCost;
        Require(errors, cost.Currency == CarrierCostCharge.Currency, "CarrierCost.Currency", "The currency must be EUR.");
        Require(errors, cost.Total >= 0 && HasCentsOnly(cost.Total), "CarrierCost.Total", "The total must be a non-negative amount in cents.");
        Require(
            errors,
            cost.ChargeLines.All(line => line.Amount >= 0 && HasCentsOnly(line.Amount)),
            "CarrierCost.ChargeLines",
            "Each charge line needs a non-negative amount in cents.");

        var source = SourceOf(cost.Source, errors);
        if (errors.Count > 0 || source is null)
        {
            return (null, errors);
        }

        return (
            new CarrierCostData(
                request.CarrierBookingNumber,
                request.CarrierBookingReference,
                new CarrierId(request.CarrierId),
                request.ShipmentNumber,
                request.TransportOrderNumber,
                new CarrierCostCharge(cost.Total, [.. cost.ChargeLines.Select(line => new ChargeLine(line.Type, line.Amount))]),
                source,
                request.DeliveryTime,
                request.CorrelationId),
            errors);
    }

    // A Carrier Rate Card source names its version and has no remark; a Spot source has the Planner's remark and no version.
    private static CarrierCostSource? SourceOf(CarrierCostSourceRequest source, Dictionary<string, string[]> errors)
    {
        const string Field = "CarrierCost.Source";
        switch (source.Kind)
        {
            case CarrierCostSourceKind.CarrierRateCard:
                if (source.Remark is not null || source.RateCardVersion is not { Version: >= 1 } version || string.IsNullOrWhiteSpace(version.RateCardId))
                {
                    errors[Field] = ["A Carrier Rate Card source needs a rateCardVersion with a rateCardId and a version of 1 or more, and no remark."];
                    return null;
                }

                return CarrierCostSource.FromRateCard(version.RateCardId, version.Version);

            default:
                if (source.RateCardVersion is not null || string.IsNullOrWhiteSpace(source.Remark) || source.Remark.Length > MaxRemarkLength)
                {
                    errors[Field] = [$"A Spot source needs the Planner's remark, of at most {MaxRemarkLength} characters, and no rateCardVersion."];
                    return null;
                }

                return CarrierCostSource.FromSpot(source.Remark);
        }
    }

    private static bool HasCentsOnly(decimal amount) => decimal.Round(amount, 2) == amount;

    private static void Require(Dictionary<string, string[]> errors, bool condition, string field, string message)
    {
        if (!condition)
        {
            errors[field] = [message];
        }
    }

    [GeneratedRegex("^CB-[0-9]{7}$")]
    private static partial Regex CarrierBookingNumber();

    [GeneratedRegex("^SH-[0-9]{7}$")]
    private static partial Regex ShipmentNumber();

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex TransportOrderNumber();
}
