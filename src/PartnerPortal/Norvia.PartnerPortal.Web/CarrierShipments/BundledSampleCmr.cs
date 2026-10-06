using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

internal sealed class BundledSampleCmr : ISampleCmr
{
    public ProofOfDeliveryUpload Read() => SampleCmr.Read();
}
