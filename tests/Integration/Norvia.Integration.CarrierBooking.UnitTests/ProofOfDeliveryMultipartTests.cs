using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class ProofOfDeliveryMultipartTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 cmr"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void Body_PutsTheMetadataFirst_AndEachFileAsAFilesPartInOrder()
    {
        var metadata = new JsonObject { ["documentId"] = "POD-1" };
        var proof = new MappedProofOfDelivery(metadata, [new("cmr.pdf", "application/pdf", Pdf), new("photo.png", "image/png", Png)]);

        var body = ProofOfDeliveryMultipart.Body(proof);

        body["$content-type"]!.GetValue<string>().Should().Be("multipart/form-data");
        var parts = body["$multipart"]!.AsArray();
        parts.Should().HaveCount(3);
        parts[0]!["headers"]!["Content-Disposition"]!.GetValue<string>().Should().Be("form-data; name=\"metadata\"");
        parts[0]!["body"]!["documentId"]!.GetValue<string>().Should().Be("POD-1");
        parts[1]!["headers"]!["Content-Disposition"]!.GetValue<string>().Should().Be("form-data; name=\"files\"; filename=\"cmr.pdf\"");
        parts[1]!["headers"]!["Content-Type"]!.GetValue<string>().Should().Be("application/pdf");
        parts[1]!["body"]!["$content-type"]!.GetValue<string>().Should().Be("application/pdf");
        Convert.FromBase64String(parts[1]!["body"]!["$content"]!.GetValue<string>()).Should().Equal(Pdf);
        parts[2]!["headers"]!["Content-Disposition"]!.GetValue<string>().Should().Contain("filename=\"photo.png\"");
        Convert.FromBase64String(parts[2]!["body"]!["$content"]!.GetValue<string>()).Should().Equal(Png);
    }

    [Fact]
    public void Body_ADoubleQuoteInAFileName_DoesNotBreakTheHeader()
    {
        var proof = new MappedProofOfDelivery(new JsonObject(), [new("a\"b.pdf", "application/pdf", Pdf)]);

        var header = ProofOfDeliveryMultipart.Body(proof)["$multipart"]![1]!["headers"]!["Content-Disposition"]!.GetValue<string>();

        header.Should().Be("form-data; name=\"files\"; filename=\"a'b.pdf\"");
    }
}
