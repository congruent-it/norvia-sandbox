using Norvia.DriverApp.Web.Reports;

namespace Norvia.DriverApp.Web.UnitTests;

/// <summary>The signature the Driver draws is posted as a PNG data URL, and the form reads only that.</summary>
public sealed class ProofOfDeliveryFormTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5];

    [Fact]
    public void APngDataUrl_IsTheSignatureAsPngBytes()
    {
        var signature = ProofOfDeliveryForm.SignatureOf("data:image/png;base64," + Convert.ToBase64String(Png));

        signature.Should().NotBeNull();
        signature!.ContentType.Should().Be("image/png");
        signature.Content.ToArray().Should().Equal(Png);
    }

    [Theory]
    [InlineData("")]
    [InlineData("data:image/jpeg;base64,/9j/4AAQ")]
    [InlineData("data:image/png;base64,")]
    [InlineData("data:image/png;base64,not base64!")]
    [InlineData("iVBORw0KGgo=")]
    public void AnythingElse_IsNoSignature(string field)
    {
        ProofOfDeliveryForm.SignatureOf(field).Should().BeNull();
    }
}
