extern alias SimulatorWeb;

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Carriers' Proofs of delivery at the Simulator's boundary (#522): the Simulator's Carriers send a readable one-page PDF through the Partner API,
/// and the TMS keeps it as the Proof of delivery's file. One run of Carrier deliveries, some of them with reservations, is read back through the TMS API.
/// </summary>
[Collection("Simulator runs")]
public sealed partial class CarrierProofOfDeliveryTests(SimulatorFixture fixture)
{
    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release.
    private const string PricedShipper = "SHP-0001";
    private const string PdfContentType = "application/pdf";
    private const string ReservationsRemark = "Two pallets short";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    // The Partner API takes a body of up to 25 MB by claim check (ADR 0023); a one-page text PDF is a few hundred bytes, so this bound is far below it.
    private const long SmallPdfBytes = 4 * 1024;

    // Every step asked for in full, and half of the tracked Shipments hit an Exception, a quarter of those with reservations.
    private static readonly CarrierShares WithReservations = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 50,
        DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDeliveriesOfACarrierRun_LeaveReadablePdfProofsOfDelivery_WithTheRemarkOnlyForReservations_AndNoFileContentInTheTelemetry()
    {
        var carrierNames = new List<string>();
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper), seed =>
            carrierNames.AddRange(seed["carriers"]!.AsArray().OfType<JsonObject>().Select(carrier => carrier["name"]!.GetValue<string>())));
        carrierNames.Should().NotBeEmpty();
        var settings = Runs.Carried(orders: 20, Runs.NextSeed(), WithReservations);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var sent = log.Where(line => line.Outcome == LogOutcome.ProofOfDeliverySent).ToList();
        var reserved = sent.Where(line => line.Detail == "With reservations").Select(line => line.ShipmentNumber!).ToHashSet();
        reserved.Should().NotBeEmpty();
        reserved.Should().HaveCountLessThan(sent.Count, "some deliveries are clean");

        var proofs = await ProofsAsync(sent, Ct);

        foreach (var line in sent)
        {
            var number = line.ShipmentNumber!;
            var proof = proofs[number];
            proof["isCurrent"]!.GetValue<bool>().Should().BeTrue($"{number} has a current Proof of delivery");
            var files = proof["files"]!.AsArray();
            var file = files.Should().ContainSingle("a Carrier's Proof of delivery is one document").Subject!.AsObject();

            // The TMS accepted the file as a PDF by its content, and keeps it as the Proof of delivery's file.
            var (contentType, content) = await fixture.ProofOfDeliveryFileAsync(number, Guid.Parse(file["id"]!.GetValue<string>()), Ct);
            contentType.Should().Be(PdfContentType);
            file["contentType"]!.GetValue<string>().Should().Be(PdfContentType);
            content.Take(PdfSignature.Length).Should().Equal(PdfSignature, "the TMS judges a file by its content, and keeps this one as a PDF");
            content.Length.Should().Be(file["sizeBytes"]!.GetValue<int>());
            content.LongLength.Should().BeInRange(1, SmallPdfBytes, "small enough for the claim check");

            var shipment = (await fixture.ShipmentAsync(number, Ct))!;
            var booking = shipment["carrierBookings"]!.AsArray().Single()!;
            var text = TextOf(content);
            text.Should().Contain("Proof of delivery")
                .And.Contain($"Shipment: {number}")
                .And.Contain($"Transport Order: {shipment["transportOrderNumber"]!.GetValue<string>()}")
                .And.Contain($"Carrier booking reference: {booking["carrierBookingReference"]!.GetValue<string>()}")
                .And.Contain($"Received by: {proof["receivedBy"]!.GetValue<string>()}");
            carrierNames.Should().Contain(text.Split('\n')[1], "the second line of the document names the Carrier");

            if (reserved.Contains(number))
            {
                text.Should().Contain("Condition: Reservations").And.Contain($"Remark: {ReservationsRemark}");
                proof["condition"]!.GetValue<string>().Should().Be("Reservations");
            }
            else
            {
                text.Should().Contain("Condition: Clean").And.NotContain("Remark:");
                proof["condition"]!.GetValue<string>().Should().Be("Clean");
            }
        }

        AssertTelemetry(simulator, sent.Count, await fixture.ResourceLogsAsync("tms-api", Ct));
    }

    [Fact]
    public async Task TwoRunsWithTheSameSeed_SendTheSameProofsOfDelivery_ApartFromTheirNumbersAndTheDeliveryTime()
    {
        var seed = Runs.NextSeed();
        var first = await ReadableProofsOfARunAsync(seed);
        var second = await ReadableProofsOfARunAsync(seed);

        first.Should().NotBeEmpty();
        second.Should().Equal(first, "the Carriers, receivers, conditions and remarks follow the seed");
    }

    // Runs the seed on a fresh Simulator and gives what each Proof of delivery says that the run and the TMS do not number, in a fixed order.
    private async Task<List<string>> ReadableProofsOfARunAsync(int seed)
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var (_, _, log) = await simulator.RunToEndAsync(Runs.Carried(orders: 12, seed, WithReservations), Ct);
        var sent = log.Where(line => line.Outcome == LogOutcome.ProofOfDeliverySent).ToList();
        var proofs = await ProofsAsync(sent, Ct);

        var texts = new List<string>();
        foreach (var line in sent)
        {
            var file = proofs[line.ShipmentNumber!]["files"]!.AsArray().Single()!;
            var (_, content) = await fixture.ProofOfDeliveryFileAsync(line.ShipmentNumber!, Guid.Parse(file["id"]!.GetValue<string>()), Ct);
            texts.Add(string.Join('\n', TextOf(content).Split('\n').Where(text => !NumberedLine().IsMatch(text))));
        }

        texts.Sort(StringComparer.Ordinal);
        return texts;
    }

    // The lines that carry what the TMS or the run numbers, or the delivery time.
    [GeneratedRegex("^(Shipment|Transport Order|Carrier booking reference|Delivered):")]
    private static partial Regex NumberedLine();

    private async Task<Dictionary<string, JsonObject>> ProofsAsync(List<LogLine> sent, CancellationToken cancellationToken)
    {
        var proofs = new Dictionary<string, JsonObject>();
        await Runs.WaitUntilAsync(async () =>
        {
            foreach (var line in sent.Where(line => !proofs.ContainsKey(line.ShipmentNumber!)))
            {
                if (await fixture.ProofOfDeliveryAsync(line.ShipmentNumber!, cancellationToken) is { } proof)
                {
                    proofs[line.ShipmentNumber!] = proof;
                }
            }

            return proofs.Count == sent.Count;
        }, cancellationToken);
        return proofs;
    }

    // Logs and traces carry the tracking keys of §7 for the Carrier's call, and never the content of the PDF.
    private static void AssertTelemetry(SimulatorHost simulator, int sent, string tmsLogs)
    {
        var entries = simulator.Telemetry.Entries;
        entries.Count(entry => entry.Contains("flow=carrier-proof-of-delivery", StringComparison.Ordinal)
                && entry.Contains("carrierBookingNumber=", StringComparison.Ordinal))
            .Should().BeGreaterThanOrEqualTo(sent, "the Simulator's trace of each Proof of delivery names its tracking keys");

        // A PDF in base64 starts with JVBERi0 ("%PDF-"), and the text of the document would show as it is.
        var all = new List<string>(entries) { tmsLogs };
        all.Should().NotContain(
            entry => entry.Contains("JVBERi0", StringComparison.Ordinal) || entry.Contains("%PDF-", StringComparison.Ordinal) || entry.Contains("Received by:", StringComparison.Ordinal),
            "a log or trace never holds the content of a file");
    }

    // The text a one-page PDF shows, one line per Tj operator: the string in brackets, with its escaped brackets and backslashes unescaped.
    private static string TextOf(byte[] pdf)
    {
        var lines = ShownText().Matches(Encoding.Latin1.GetString(pdf)).Select(match => Unescape(match.Groups[1].Value));
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex ShownText();

    private static string Unescape(string text) => Regex.Replace(text, @"\\(.)", "$1");
}
