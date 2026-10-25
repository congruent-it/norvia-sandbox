using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

public sealed class EvidenceMaskerTests
{
    [Theory]
    [InlineData("CARRIER_ENDPOINT_KEY=abc123", "CARRIER_ENDPOINT_KEY=***")]
    [InlineData("password: hunter2", "password: ***")]
    [InlineData("clientSecret = \"quoted value\" is set", "clientSecret = *** is set")]
    [InlineData("Endpoint=sb://x;SharedAccessKey=AbC/dEf+==;Other=1", "Endpoint=sb://x;SharedAccessKey=***")]
    [InlineData("sent Authorization: Bearer eyJhbGciOi.payload.sig", "sent Authorization: Bearer ***")]
    [InlineData("calls with Bearer eyJhbGciOi.payload.sig", "calls with Bearer ***")]
    public void Of_HidesWhatFollowsASecretsName_AndABearerToken(string text, string expected) =>
        EvidenceMasker.Of(text).Should().Be(expected);

    [Theory]
    [InlineData("No alert watches the dead-letter queue of carrier-tracking.")]
    [InlineData("carrier-tracking")]
    [InlineData("")]
    public void Of_LeavesTextWithoutASecretAsItWas(string text) =>
        EvidenceMasker.Of(text).Should().Be(text);

    [Fact]
    public void Of_NullText_Throws()
    {
        var mask = () => EvidenceMasker.Of(null!);

        mask.Should().Throw<ArgumentNullException>();
    }
}
