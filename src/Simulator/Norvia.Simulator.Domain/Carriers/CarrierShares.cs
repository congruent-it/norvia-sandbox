namespace Norvia.Simulator.Domain.Carriers;

/// <summary>
/// The Carrier stage of the funnel, one percentage per step, each a share of the step before it: of the Shipments assigned to a
/// Carrier, the share the Planner releases; of the released, the share confirmed and declined (the rest give no answer); of the
/// confirmed, the share tracked; of the tracked, the share that hit an Exception; of the tracked that didn't fail, the share
/// delivered; of the delivered, the share with a Proof of delivery; of the failed collections, the share the Planner cancels.
/// </summary>
public sealed record CarrierShares(
    int ReleasePercent = CarrierShares.DefaultReleasePercent,
    int ConfirmPercent = CarrierShares.DefaultConfirmPercent,
    int DeclinePercent = CarrierShares.DefaultDeclinePercent,
    int TrackedPercent = CarrierShares.DefaultTrackedPercent,
    int ExceptionPercent = CarrierShares.DefaultExceptionPercent,
    int DeliveredPercent = CarrierShares.DefaultDeliveredPercent,
    int ProofOfDeliveryPercent = CarrierShares.DefaultProofOfDeliveryPercent,
    int CancelPercent = CarrierShares.DefaultCancelPercent)
{
    public const int DefaultReleasePercent = 90;
    public const int DefaultConfirmPercent = 70;
    public const int DefaultDeclinePercent = 15;
    public const int DefaultTrackedPercent = 90;
    public const int DefaultExceptionPercent = 10;
    public const int DefaultDeliveredPercent = 80;
    public const int DefaultProofOfDeliveryPercent = 85;
    public const int DefaultCancelPercent = 50;

    /// <summary>Everything wrong with these shares; empty when they may be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Check(problems, ReleasePercent, "The share of Shipments the Planner releases");
        Check(problems, ConfirmPercent, "The share of released Shipments the Carriers confirm");
        Check(problems, DeclinePercent, "The share of released Shipments the Carriers decline");
        Check(problems, TrackedPercent, "The share of confirmed Shipments the Carriers track");
        Check(problems, ExceptionPercent, "The share of tracked Shipments that hit an Exception");
        Check(problems, DeliveredPercent, "The share of tracked Shipments that are delivered");
        Check(problems, ProofOfDeliveryPercent, "The share of delivered Shipments with a Proof of delivery");
        Check(problems, CancelPercent, "The share of failed collections the Planner cancels");

        if (ConfirmPercent is >= 0 and <= 100 && DeclinePercent is >= 0 and <= 100 && ConfirmPercent + DeclinePercent > 100)
        {
            problems.Add("The Carriers can't confirm and decline more than all of the released Shipments between them.");
        }

        return problems;
    }

    private static void Check(List<string> problems, int percent, string what)
    {
        if (percent is < 0 or > 100)
        {
            problems.Add($"{what} is a percentage from 0 to 100.");
        }
    }
}
