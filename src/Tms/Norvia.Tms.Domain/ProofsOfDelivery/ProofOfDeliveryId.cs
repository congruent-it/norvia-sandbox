namespace Norvia.Tms.Domain.ProofsOfDelivery;

public readonly record struct ProofOfDeliveryId(Guid Value)
{
    public static ProofOfDeliveryId New() => new(Guid.CreateVersion7());
}
