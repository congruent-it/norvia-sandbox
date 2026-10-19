using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierInvoiceFormatTests
{
    [Fact]
    public void ToJson_IsTheCarrierInvoiceFormat_WithALocalDate_WithoutNamingTheCarrier()
    {
        var json = CarrierInvoiceFormat.ToJson(
            "VDT-2026-0412", new DateOnly(2026, 10, 20),
            [new CarrierInvoiceLineDraft("TRX-998", 70.50m), new CarrierInvoiceLineDraft(" TRX-999 ", 120m, " Delivered Monday ")]);

        JsonNode.DeepEquals(json, JsonNode.Parse("""
            {
              "invoiceNo": "VDT-2026-0412", "invoiceDate": "20-10-2026",
              "lines": [ { "bookingRef": "TRX-998", "amount": 70.50 }, { "bookingRef": "TRX-999", "amount": 120, "description": "Delivered Monday" } ]
            }
            """)).Should().BeTrue(json.ToJsonString());
        Contracts.CarrierInvoiceSchemaErrors(json).Should().BeEmpty();
        json.ToJsonString().Should().NotContain("CAR-");
    }

    [Fact]
    public void ToJson_ABlankDescription_IsLeftOut()
    {
        var json = CarrierInvoiceFormat.ToJson("VDT-1", new DateOnly(2026, 10, 20), [new CarrierInvoiceLineDraft("TRX-998", 70.50m, "  ")]);

        json["lines"]![0]!.AsObject().ContainsKey("description").Should().BeFalse();
    }
}
