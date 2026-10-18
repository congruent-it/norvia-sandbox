using System.Net;
using Azure.Storage.Blobs;

namespace Norvia.Tms.Api.IntegrationTests.Reset;

/// <summary>
/// The Simulator's reset at the TMS API (ADR 0039): with the setting on and the Simulator's role, the TMS deletes its transactional records and the
/// Proof of delivery files and keeps the reference data its seed file gives it. The tests share one TMS, which each of them empties.
/// </summary>
public sealed class TmsResetTests(ResetTmsFixture tms) : IClassFixture<ResetTmsFixture>
{
    private const string ProofContainer = "proofs-of-delivery";
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetWithTheResetRole_DeletesEveryKindOfTransactionalRecord_AndTheProofOfDeliveryFile()
    {
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));
        var orderNumber = await tms.SubmitAcceptedAsync(_cancellationToken);
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        await tms.RecordedProofAsync(ProofOfDeliveryCalls.Metadata(reference), [ProofOfDeliveryCalls.Cmr()], _cancellationToken);
        var (failedShipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var tripNumber = await tms.TmsUi.DraftTripAsync(CapacityCalls.DateOf((await tms.ListOpenDaysAsync(_cancellationToken))[1]), _cancellationToken);
        var proofs = new BlobServiceClient(await tms.ConnectionStringAsync("blobs", _cancellationToken)).GetBlobContainerClient(ProofContainer);
        (await Blobs(proofs)).Should().NotBeEmpty("the Proof of delivery file is stored before the reset");
        (await tms.ListOpenExceptionsAsync(_cancellationToken)).Should().NotBeEmpty();

        var deleted = await simulator.ResetAsync(_cancellationToken);

        foreach (var kind in new[] { "Transport Orders", "Shipments", "Trips", "Exceptions", "Carrier bookings", "Tracking events", "Proof of delivery records", "Messages", "Proof of delivery files" })
        {
            deleted.Should().ContainKey(kind);
            deleted[kind].Should().BePositive($"the reset deleted {kind}");
        }

        (await tms.Tms.StatusOfAsync($"/v1/transport-orders/{orderNumber}", _cancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await tms.Tms.StatusOfAsync($"/v1/shipments/{shipmentNumber}", _cancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await tms.Tms.StatusOfAsync($"/v1/shipments/{shipmentNumber}/proof-of-delivery", _cancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await tms.Tms.StatusOfAsync($"/v1/shipments/{failedShipmentNumber}", _cancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await tms.TmsUi.StatusOfAsync($"/v1/trips/{tripNumber}", _cancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await tms.ListOpenExceptionsAsync(_cancellationToken)).Should().BeEmpty();
        (await tms.TmsUi.ListAsync("/v1/trips", _cancellationToken)).Should().BeEmpty();
        (await Blobs(proofs)).Should().BeEmpty("the reset deleted the Proof of delivery files too");
    }

    [Fact]
    public async Task AfterAReset_TheNumbersStartAgain_AndTheSeededShippersCarriersAndRateCardsStillWork()
    {
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));
        await tms.SubmitAcceptedAsync(_cancellationToken);

        await simulator.ResetAsync(_cancellationToken);

        // Rated needs the seeded Shipper and its Rate Card; assigning needs the seeded Carrier.
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        number.Should().Be("TO-0000001");
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        assigned["shipmentNumber"]!.GetValue<string>().Should().Be("SH-0000001");
    }

    [Fact]
    public async Task AReset_PutsTheSeedFilesRostersBack_AndRemovesThePlannersAbsencesAndOffTheRoadDays_ButKeepsTheVehiclesAndDrivers()
    {
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));
        var seededRosters = await tms.TmsUi.ListAsync("/v1/capacity/rosters", _cancellationToken);
        var seededWeekdays = WeekdaysOf(seededRosters, "DRV-0002");
        var days = await tms.ListOpenDaysAsync(_cancellationToken);
        var day = CapacityCalls.DateOf(days[3]);
        var vehiclesBefore = days[3]["availableVehicles"]!.GetValue<int>();
        var driversBefore = DriversOn(days[3]);
        vehiclesBefore.Should().BePositive("the seed has Vehicles");
        driversBefore.Should().NotBeEmpty("the seed has Drivers with Shifts");
        using var absence = await tms.RecordPeriodAsync("/v1/capacity/absences", "DRV-0001", day, day, "Leave", _cancellationToken);
        using var offTheRoad = await tms.RecordPeriodAsync("/v1/capacity/off-the-road-days", "VEH-0001", day, day, "Repair", _cancellationToken);
        using var roster = await tms.ChangeRosterAsync("DRV-0002", [DayOfWeek.Monday], "09:00", "17:00", _cancellationToken);
        absence.StatusCode.Should().Be(HttpStatusCode.OK);
        offTheRoad.StatusCode.Should().Be(HttpStatusCode.OK);
        roster.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekdaysOf(await tms.TmsUi.ListAsync("/v1/capacity/rosters", _cancellationToken), "DRV-0002").Should().Be(1);

        await simulator.ResetAsync(_cancellationToken);

        (await tms.TmsUi.ListAsync("/v1/capacity/absences", _cancellationToken)).Should().BeEmpty();
        (await tms.TmsUi.ListAsync("/v1/capacity/off-the-road-days", _cancellationToken)).Should().BeEmpty();
        // The day the Absence and the Off the road day shaped is opened again from the seed file's Rosters.
        var dayAfter = (await tms.ListOpenDaysAsync(_cancellationToken)).Single(open => CapacityCalls.DateOf(open) == day);
        dayAfter["availableVehicles"]!.GetValue<int>().Should().Be(vehiclesBefore, "the Vehicles are reference data and stay, with the Off the road day gone");
        DriversOn(dayAfter).Should().BeEquivalentTo(driversBefore, "the Drivers stay, with their Absence and the Roster change gone");
        var rosters = await tms.TmsUi.ListAsync("/v1/capacity/rosters", _cancellationToken);
        WeekdaysOf(rosters, "DRV-0002").Should().Be(seededWeekdays);
        rosters.Select(item => item!["driverId"]!.GetValue<string>()).Should().Contain(["DRV-0001", "DRV-0002"]);
    }

    [Fact]
    public async Task AResetOnAnEmptyTms_Succeeds_WithNothingDeleted()
    {
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));
        await simulator.ResetAsync(_cancellationToken);

        var deleted = await simulator.ResetAsync(_cancellationToken);

        deleted["Transport Orders"].Should().Be(0);
        deleted["Shipments"].Should().Be(0);
        deleted["Trips"].Should().Be(0);
        deleted["Proof of delivery files"].Should().Be(0);
    }

    [Fact]
    public async Task AResetWithoutTheResetRole_IsRefusedWith403_AndDeletesNothing()
    {
        var orderNumber = await tms.SubmitAcceptedAsync(_cancellationToken);
        using var noRole = tms.CreateTmsClient(tms.IssueToken([]));

        // The Planner's identity manages Transport Orders and is still no caller for the reset.
        using var response = await tms.TmsUi.ResetOnceAsync(_cancellationToken);
        using var unauthorised = await noRole.ResetOnceAsync(_cancellationToken);
        using var anonymous = tms.CreateTmsClient(token: null);
        using var anonymousResponse = await anonymous.ResetOnceAsync(_cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unauthorised.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await tms.Tms.StatusOfAsync($"/v1/transport-orders/{orderNumber}", _cancellationToken)).Should().Be(HttpStatusCode.OK);
    }

    private static async Task<IReadOnlyList<string>> Blobs(BlobContainerClient container)
    {
        if (!await container.ExistsAsync(TestContext.Current.CancellationToken))
        {
            return [];
        }

        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    private static List<string> DriversOn(System.Text.Json.Nodes.JsonObject day) =>
        [.. day["shifts"]!.AsArray().Select(shift => shift!["driverName"]!.GetValue<string>())];

    private static int WeekdaysOf(System.Text.Json.Nodes.JsonArray rosters, string driverId) =>
        rosters.Single(roster => roster!["driverId"]!.GetValue<string>() == driverId)!["weekdays"]!.AsArray().Count;
}
