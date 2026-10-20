using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.CarrierCosts;

/// <summary>
/// Recording a Carrier cost through the ERP billing API (E2, user story 1): it is recorded as Open with its charge lines and source, a repeat is the original,
/// a Carrier booking recorded with different content is a conflict, and only the Carrier cost Logic App's identity may record.
/// </summary>
public sealed class CarrierCostTests(ErpFixture fixture) : CarrierTest(fixture)
{
    [Fact]
    public async Task ACarrierCost_IsRecordedAsOpen_WithItsChargeLinesAndSource_AndListedUnderItsCarrier()
    {
        using var response = await PostCostAsync(CarrierCost("CB-0000201", "TRX-201", 301.4m, rateCard: true));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var result = await BodyOfAsync(response);
        result["repeat"]!.GetValue<bool>().Should().BeFalse();
        var cost = result["cost"]!.AsObject();
        cost["status"]!.GetValue<string>().Should().Be("Open");
        cost["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000201");
        cost["total"]!.GetValue<decimal>().Should().Be(301.4m);
        cost["source"]!["kind"]!.GetValue<string>().Should().Be("CarrierRateCard");
        var open = (await Finance.AskAsync(new Application.CarrierCosts.ListOpen.ListOpenCarrierCostsQuery(), Ct)).Should().ContainSingle().Subject;
        open.CarrierId.Should().Be(Carrier);
        open.Total.Should().Be(301.4m);
        open.Costs.Single().Source.Should().Be(new Application.CarrierCosts.CarrierCostSourceView("CarrierRateCard", "CRC-0001", 2, null));
    }

    [Fact]
    public async Task ASpotCarrierCost_KeepsThePlannersRemark()
    {
        using var response = await PostCostAsync(CarrierCost("CB-0000202", "TRX-202", 95m));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var open = (await Finance.AskAsync(new Application.CarrierCosts.ListOpen.ListOpenCarrierCostsQuery(), Ct)).Single().Costs.Single();
        open.Source.Should().Be(new Application.CarrierCosts.CarrierCostSourceView("Spot", null, null, "Spot price agreed by phone."));
    }

    [Fact]
    public async Task ARepeatOfACarrierCost_AnswersTheOriginalWith200_AndRecordsItOnce()
    {
        var cost = CarrierCost("CB-0000203", "TRX-203", 120m);
        using var first = await PostCostAsync(cost);

        using var repeat = await PostCostAsync(cost);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOfAsync(repeat))["repeat"]!.GetValue<bool>().Should().BeTrue();
        (await OpenCostsAsync()).Should().Equal("TRX-203");
        (await Fixture.PublishedSettledAsync(Ct)).Should().BeEmpty("recording a Carrier cost publishes nothing");
    }

    [Fact]
    public async Task ACarrierBookingRecordedWithDifferentContent_IsRefusedWith409_AndKeepsTheOriginal()
    {
        await RecordCostAsync("CB-0000204", "TRX-204", 120m);

        using var response = await PostCostAsync(CarrierCost("CB-0000204", "TRX-204", 130m));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).Should().Be("CarrierCostConflict");
        (await Finance.AskAsync(new Application.CarrierCosts.ListOpen.ListOpenCarrierCostsQuery(), Ct)).Single().Costs.Single().Total.Should().Be(120m);
    }

    [Fact]
    public async Task ACarrierCostOfACarrierTheErpDoesNotHave_IsRefusedWith422()
    {
        using var response = await PostCostAsync(CarrierCost("CB-0000205", "TRX-205", 120m, carrier: "CAR-9999"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).Should().Be("CarrierNotFound");
        (await OpenCostsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ACarrierCostWhoseChargeLinesDoNotAddUpToItsTotal_IsRefusedWith400()
    {
        var cost = CarrierCost("CB-0000206", "TRX-206", 120m);
        cost["carrierCost"]!["chargeLines"]![0]!["amount"] = 100m;

        using var response = await PostCostAsync(cost);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierCostTotalMismatch");
        (await OpenCostsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AMessageThatIsNotACarrierCostIncurred_IsRefusedWith400()
    {
        var cost = CarrierCost("CB-0000207", "TRX-207", 120m);
        cost["somethingElse"] = "outside the Canonical Model";

        using var response = await PostCostAsync(cost);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OpenCostsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerWithoutTheRecordCarrierCostRole_IsRefused_AndRecordsNothing()
    {
        foreach (var roles in new string[][]
        {
            [ErpFixture.RecordRole],
            [ErpFixture.ReceiveCarrierInvoiceRole],
            [ErpFixture.ReportCarrierInvoiceRole],
            [ErpFixture.ManageCarrierInvoiceRole],
        })
        {
            using var response = await PostCostAsync(CarrierCost("CB-0000208", "TRX-208", 120m), roles);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{string.Join(',', roles)} isn't {ErpFixture.RecordCarrierCostRole}");
        }

        (await OpenCostsAsync()).Should().BeEmpty();
    }
}
