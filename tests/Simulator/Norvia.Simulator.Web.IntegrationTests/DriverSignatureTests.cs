extern alias SimulatorWeb;

using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Drivers' Proofs of delivery at the Simulator's boundary (#525): the Simulator's Drivers deliver through the real Driver app API, and the TMS keeps what
/// they sent as the Proof of delivery. One run of the Own fleet's Trips of today, half of its deliveries with reservations, is read back through the TMS API.
/// </summary>
[Collection("Simulator runs")]
public sealed class DriverSignatureTests(SimulatorFixture fixture)
{
    // The Own fleet of this class: no other test class puts Trips on today for them.
    private const string Shipper = "SHP-0001";
    private const string Vehicle = "VEH-0001";
    private const string Driver = "DRV-0001";

    // What the Driver app asks of the files (P12, P6): a PNG signature, JPEG or PNG photos, each of at most 10 MB.
    private const long MaximumFileBytes = 10L * 1024 * 1024;
    private const string PngContentType = "image/png";
    private const string FallbackReceiver = "Simulated consignee";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly DriverShares HalfWithReservations = new(CollectionFailedPercent: 0, DeliveryFailedPercent: 0, DamagedPercent: 0, ReservationsPercent: 50);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDeliveriesOfARun_LeaveProofsOfDeliveryWithDrawnSignatures_PhotosOnlyWithReservations_AndNoFileContentInTheTelemetry()
    {
        // The Drivers' rosters are Monday to Friday; at the weekend the Driver works today by a change of Roster, so the test runs on any day.
        await using var worksToday = await fixture.WorkTodayAsync(Driver, Ct);
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(Shipper), SeedEdits.OwnFleetOf(Vehicle, Driver));
        var seed = Runs.NextSeed();
        var settings = Runs.Tripped(orders: 8, seed, collectedTodayPercent: 100, HalfWithReservations);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, Runs.TripPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var delivered = log.Where(line => line.Action == DriverActions.Deliver && line.Outcome == LogOutcome.DriverReported).ToList();
        delivered.Should().HaveCountGreaterThan(1, "the Own fleet's Vehicle carries some of the day's Shipments");
        var withReservations = delivered.Where(line => line.Detail!.Contains("With reservations", StringComparison.Ordinal)).Select(line => line.ShipmentNumber!).ToHashSet();
        withReservations.Should().NotBeEmpty();
        withReservations.Should().HaveCountLessThan(delivered.Count, "half of the deliveries are clean");

        var proofs = new Dictionary<string, JsonObject>();
        await Runs.WaitUntilAsync(async () =>
        {
            foreach (var line in delivered.Where(line => !proofs.ContainsKey(line.ShipmentNumber!)))
            {
                if (await fixture.ProofOfDeliveryAsync(line.ShipmentNumber!, Ct) is { } proof)
                {
                    proofs[line.ShipmentNumber!] = proof;
                }
            }

            return proofs.Count == delivered.Count;
        }, Ct);

        var signatures = new List<byte[]>();
        foreach (var (number, proof) in proofs)
        {
            proof["isCurrent"]!.GetValue<bool>().Should().BeTrue($"{number} has a current Proof of delivery");
            proof["source"]!.GetValue<string>().Should().Be("Own fleet");
            var reserved = withReservations.Contains(number);
            proof["condition"]!.GetValue<string>().Should().Be(reserved ? "Reservations" : "Clean");
            proof["receivedBy"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace().And.NotBe(FallbackReceiver, "the receiver is the Consignee's contact of the booking");

            // A clean delivery has the signature alone; one with reservations has a photo as well.
            var files = proof["files"]!.AsArray().Select(file => file!.AsObject()).ToList();
            files.Should().HaveCount(reserved ? 2 : 1);
            var signature = files.Single(file => file["name"]!.GetValue<string>() == "signature.png");
            files.Count(file => file["name"]!.GetValue<string>().StartsWith("photo-", StringComparison.Ordinal)).Should().Be(reserved ? 1 : 0);

            foreach (var file in files)
            {
                file["contentType"]!.GetValue<string>().Should().Be(PngContentType, "the TMS keeps the type the Driver app accepted");
                file["sizeBytes"]!.GetValue<long>().Should().BeInRange(1, MaximumFileBytes, "within the Driver app's rules for a file");

                // Read back through the TMS file endpoint, with the content type it was sent with.
                var (contentType, content) = await fixture.ProofOfDeliveryFileAsync(number, Guid.Parse(file["id"]!.GetValue<string>()), Ct);
                contentType.Should().Be(PngContentType);
                content.Length.Should().Be(file["sizeBytes"]!.GetValue<int>());
                content.Take(PngSignature.Length).Should().Equal(PngSignature, "the file is a PNG");
            }

            var signatureBytes = (await fixture.ProofOfDeliveryFileAsync(number, Guid.Parse(signature["id"]!.GetValue<string>()), Ct)).Content;
            var (width, height) = SizeOf(signatureBytes);
            width.Should().BeGreaterThan(1);
            height.Should().BeGreaterThan(1);

            // The same seed and Shipment number give the same bytes in every run, so what the TMS holds is what the Simulator draws for them.
            signatureBytes.Should().Equal(DeliveryFiles.Signature(seed, number));
            if (reserved)
            {
                var photo = files.Single(file => file["name"]!.GetValue<string>().StartsWith("photo-", StringComparison.Ordinal));
                (await fixture.ProofOfDeliveryFileAsync(number, Guid.Parse(photo["id"]!.GetValue<string>()), Ct)).Content.Should().Equal(DeliveryFiles.Photo(seed, number));
            }

            signatures.Add(signatureBytes);
        }

        signatures.Select(Convert.ToHexString).Distinct().Should().HaveCount(signatures.Count, "no two deliveries get the same signature");
        proofs.Values.Select(proof => proof["receivedBy"]!.GetValue<string>()).Distinct().Should().HaveCountGreaterThan(1, "the receiver differs per delivery");

        await AssertTrackingKeysAsync(simulator, delivered);
    }

    // Logs and traces carry the tracking keys of §7 for the delivery, and never the content of a file.
    private async Task AssertTrackingKeysAsync(SimulatorHost simulator, IReadOnlyList<LogLine> delivered)
    {
        var simulatorEntries = simulator.Telemetry.Entries;
        foreach (var line in delivered)
        {
            simulatorEntries.Should().Contain(
                entry => entry.Contains($"shipmentNumber={line.ShipmentNumber}", StringComparison.Ordinal)
                    && entry.Contains($"tripNumber={line.TripNumber}", StringComparison.Ordinal)
                    && entry.Contains("flow=trip-execution", StringComparison.Ordinal),
                $"the Simulator's trace of the delivery of {line.ShipmentNumber} names its tracking keys");
        }

        var all = new List<string>(simulatorEntries);
        foreach (var resource in new[] { "driver-app-api", "tms-api" })
        {
            all.Add(await fixture.ResourceLogsAsync(resource, Ct));
        }

        // A PNG in base64 starts with iVBORw0KGgo, and JSON would carry the file that way; raw, it starts with its signature bytes.
        all.Should().NotContain(entry => entry.Contains("iVBORw0KGgo", StringComparison.Ordinal) || entry.Contains("\u0089PNG", StringComparison.Ordinal),
            "a log or trace never holds the content of a file");
    }

    // The width and height in a PNG's header chunk, which follows its 8-byte signature and the chunk's length and type.
    private static (int Width, int Height) SizeOf(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
}
