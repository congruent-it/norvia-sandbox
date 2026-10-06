namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

/// <summary>The sample CMR a Persona in auto-progress sends with its Proof of delivery, having no file of its own.</summary>
public interface ISampleCmr
{
    ProofOfDeliveryUpload Read();
}
