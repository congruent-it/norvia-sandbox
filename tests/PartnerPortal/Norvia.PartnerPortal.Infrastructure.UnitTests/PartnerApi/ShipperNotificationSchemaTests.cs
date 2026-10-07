using System.Text.Json;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class ShipperNotificationSchemaTests
{
    [Fact]
    public void ExceptionCode_OfTheSchema_IsEveryCodeThePortalTakes()
    {
        using var schema = JsonDocument.Parse(Contracts.Read("PartnerApi/shipper-notification.v1.schema.json"));

        var codes = schema.RootElement.GetProperty("properties").GetProperty("exceptionCode").GetProperty("enum").EnumerateArray().Select(code => code.GetString());

        codes.Should().BeEquivalentTo(ShipperExceptionCodes.WireNames);
    }
}
