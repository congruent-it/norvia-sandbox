using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>What the Carrier Proof of delivery Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class ProofOfDeliveryWorkerOptions
{
    public const string SectionName = "ProofOfDelivery";

    /// <summary>The Service Bus queue the Carrier Proof of delivery Logic App's trigger reads.</summary>
    [Required]
    public string Queue { get; set; } = "carrier-proof-of-delivery";

    /// <summary>The blob container the claim check's blobs are in; the message names it too, but the Logic App reads only this one.</summary>
    [Required]
    public string InboundContainer { get; set; } = "inbound-proofs-of-delivery";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Carrier Proof of delivery Logic App's token for the TMS, with the role <c>ProofsOfDelivery.Record</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();
}
