using System.Text;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class MessageContentTests
{
    private const string Json = """{ "carrierBookingNumber": "CB-0000001", "note": "Getränke" }""";

    [Fact]
    public void Text_Base64Content_IsDecoded() =>
        MessageContent.Text(Convert.ToBase64String(Encoding.UTF8.GetBytes(Json))).Should().Be(Json);

    [Theory]
    [InlineData(Json)]
    [InlineData("   " + Json)]
    public void Text_JsonText_PassesThrough(string content) =>
        MessageContent.Text(content).Should().Be(content);

    [Fact]
    public void Text_ContentThatIsNeitherBase64NorJson_PassesThroughForTheMapToRefuse() =>
        MessageContent.Text("not base64 !").Should().Be("not base64 !");

    [Fact]
    public void Text_WithoutContent_Throws() =>
        ((Action)(() => MessageContent.Text(null!))).Should().Throw<ArgumentNullException>();
}
