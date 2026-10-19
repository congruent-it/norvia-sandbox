using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Erp.Application.BillableLines.ListWaiting;

namespace Norvia.Erp.Api.IntegrationTests.Billing;

/// <summary>Recording a Billable line at the ERP billing API (story 1, 10 and 11): idempotent on the Transport Order number, and refused with a stable code when it can't be taken.</summary>
public sealed class RecordBillableLineTests(ErpFixture fixture) : ErpTest(fixture)
{
    [Fact]
    public async Task Record_ANewLine_Answers201_AndTheLineWaitsWithItsFreightChargeAndCorrelationIds()
    {
        var billable = Billable("TO-0000101", freight: 96.40m, minimumChargeAdjustment: 53.60m, releasedWithoutProofOfDelivery: true);

        var recorded = await RecordAsync(billable);

        recorded["repeat"]!.GetValue<bool>().Should().BeFalse();
        var line = (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle().Subject;
        line.TransportOrderNumber.Should().Be("TO-0000101");
        line.Status.Should().Be("Waiting");
        line.InvoiceNumber.Should().BeNull();
        line.ShipperReference.Should().Be("REF-TO-0000101");
        line.ReleasedWithoutProofOfDelivery.Should().BeTrue();
        line.FreightCharge.Total.Should().Be(150m);
        line.FreightCharge.ChargeLines.Select(charge => (charge.Type, charge.Amount))
            .Should().Equal(("Freight", 96.40m), ("MinimumChargeAdjustment", 53.60m));
        line.BillingCorrelationId.Should().Be(billable["correlationId"]!.GetValue<string>());
        line.IntakeCorrelationId.Should().Be(billable["intakeCorrelationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Record_TheSameLineAgain_Answers200WithTheOriginalLine_AndRecordsItOnce()
    {
        var billable = Billable("TO-0000102");
        await RecordAsync(billable);

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var repeat = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, Ct);

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOfAsync(repeat);
        body["repeat"]!.GetValue<bool>().Should().BeTrue();
        body["line"]!["transportOrderNumber"]!.GetValue<string>().Should().Be("TO-0000102");
        (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle();
    }

    [Fact]
    public async Task Record_AResubmissionWithAnotherEventIdButTheSameContent_IsARepeat()
    {
        var billable = Billable("TO-0000103");
        await RecordAsync(billable);
        var resubmitted = JsonNode.Parse(billable.ToJsonString())!.AsObject();
        resubmitted["eventId"] = Guid.NewGuid().ToString();

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), resubmitted, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle();
    }

    [Fact]
    public async Task Record_AnotherLineForTheSameTransportOrder_IsRefusedWithBillableLineConflict_AndTheFirstStays()
    {
        await RecordAsync(Billable("TO-0000104", freight: 100m));

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), Billable("TO-0000104", freight: 120m), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).Should().Be("BillableLineConflict");
        (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle().Which.FreightCharge.Total.Should().Be(100m);
    }

    [Fact]
    public async Task Record_ALineForAShipperTheErpDoesNotKnow_IsRefusedWithShipperNotFound()
    {
        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), Billable("TO-0000105", shipperId: "SHP-9999"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).Should().Be("ShipperNotFound");
        (await Finance.AskAsync(new ListWaitingBillableLinesQuery(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_ChargeLinesThatDoNotAddUpToTheTotal_AreRefusedWithFreightChargeTotalMismatch()
    {
        var billable = Billable("TO-0000106", freight: 100m);
        billable["freightCharge"]!["total"] = 130m;

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("FreightChargeTotalMismatch");
    }

    [Fact]
    public async Task Record_AFreightChargeWithoutChargeLines_IsRefusedWithFreightChargeWithoutChargeLines()
    {
        var billable = Billable("TO-0000107");
        billable["freightCharge"]!["chargeLines"] = new JsonArray();

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("FreightChargeWithoutChargeLines");
    }

    [Fact]
    public async Task Record_AMemberOutsideTheCanonicalModel_IsRefused_AndNothingIsRecorded()
    {
        var billable = Billable("TO-0000108");
        billable["carrierId"] = "CAR-0001";

        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Finance.AskAsync(new ListWaitingBillableLinesQuery(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_ALineWithABadTransportOrderNumber_IsRefusedAsAnInvalidRequest()
    {
        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), Billable("TO-42"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Finance.AskAsync(new ListWaitingBillableLinesQuery(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Waiting_LinesOfTwoShippers_AreListedPerShipperWithTheirCountAndTotal_AndAShipperWithoutLinesIsLeftOut()
    {
        await RecordAsync(Billable("TO-0000111", Brouwer, freight: 100m));
        await RecordAsync(Billable("TO-0000112", Brouwer, freight: 50.25m));
        await RecordAsync(Billable("TO-0000113", Groenrijk, freight: 75m));

        var waiting = await Finance.AskAsync(new ListWaitingBillableLinesQuery(), Ct);

        waiting.Select(shipper => (shipper.ShipperId, shipper.LineCount, shipper.Total))
            .Should().BeEquivalentTo([(Brouwer, 2, 150.25m), (Groenrijk, 1, 75m)]);
    }
}
