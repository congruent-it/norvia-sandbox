namespace Norvia.Simulator.Domain.Carriers;

/// <summary>What a Carrier's calls are called: the log's action, and the span the Simulator sends the call in.</summary>
public static class CarrierActions
{
    public const string Answer = "Answer Carrier booking";

    public const string Track = "Send tracking";

    public const string ProveDelivery = "Send Proof of delivery";
}
