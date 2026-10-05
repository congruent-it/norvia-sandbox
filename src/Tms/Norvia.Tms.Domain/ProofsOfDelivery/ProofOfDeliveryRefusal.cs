namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>Why the TMS refused a Proof of delivery: a stable code and a readable message.</summary>
public sealed record ProofOfDeliveryRefusal(ProofOfDeliveryRefusalCode Code, string Message);
