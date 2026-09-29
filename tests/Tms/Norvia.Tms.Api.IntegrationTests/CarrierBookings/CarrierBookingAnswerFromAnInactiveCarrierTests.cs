using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// Whether a Carrier is active matters only when the Planner assigns it (P3), not when it answers: a Carrier made
/// inactive after its booking was sent can still confirm or reject it.
/// </summary>
public sealed class CarrierBookingAnswerFromAnInactiveCarrierTests(CorrectableSeedTmsFixture tms) : IClassFixture<CorrectableSeedTmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Confirm_FromACarrierMadeInactiveSinceTheBookingWasSent_IsAccepted()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var released = await tms.ReleasedAsync(assigned["shipmentNumber"]!.GetValue<string>(), _cancellationToken);
        var carrierBookingNumber = released["carrierBookingNumber"]!.GetValue<string>();

        await tms.CorrectSeedAsync(Deactivate(PlanningCalls.CarrierId), _cancellationToken);

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "VDT-240917-0042", _cancellationToken);

        confirmed["status"]!.GetValue<string>().Should().Be("Confirmed");
    }

    private static Action<JsonObject> Deactivate(string carrierId) => seed =>
        seed["carriers"]!.AsArray().Single(carrier => carrier!["id"]!.GetValue<string>() == carrierId)!["active"] = false;
}
