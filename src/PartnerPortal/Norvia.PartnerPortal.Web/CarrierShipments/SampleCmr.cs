using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>The sample CMR bundled with the portal, for a Proof of delivery whose Carrier has no file of its own to send.</summary>
public static class SampleCmr
{
    public const string FileName = "sample-cmr.pdf";

    public static ProofOfDeliveryUpload Read()
    {
        using var stream = typeof(SampleCmr).Assembly.GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException($"{FileName} is not embedded.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return new ProofOfDeliveryUpload(FileName, "application/pdf", content.ToArray());
    }
}
