using System.Globalization;
using Norvia.Tms.Api.IntegrationTests.Exceptions;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// Customer service's list of Transport Orders not yet billable (P9): the Delivered Transport-only orders that aren't
/// Billable, with each blocking reason, oldest delivery first.
/// </summary>
public sealed class NotYetBillableListTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeliveredOrderWithoutAProofOfDelivery_IsListedWithItsShipperReferenceDeliveryTimeAndTheReason()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var entry = await tms.NotYetBillableEntryAsync(order, _cancellationToken);

        entry.Should().NotBeNull();
        var stored = await tms.GetTransportOrderAsync(order, _cancellationToken);
        entry!["shipper"]!["id"]!.GetValue<string>().Should().Be("SHP-0001");
        entry["shipperReference"]!.GetValue<string>().Should().Be(stored["transportOrder"]!["shipperReference"]!.GetValue<string>());
        entry["shipmentNumber"]!.GetValue<string>().Should().Be(booked.ShipmentNumber);
        var deliveryTime = (await tms.GetTimelineAsync(booked.ShipmentNumber, _cancellationToken))["trackingEvents"]!.AsArray()
            .Single(e => e!["type"]!.GetValue<string>() == "Delivered")!["eventTime"]!.GetValue<string>();
        DateTimeOffset.Parse(entry["deliveredAt"]!.GetValue<string>(), CultureInfo.InvariantCulture)
            .Should().Be(DateTimeOffset.Parse(deliveryTime, CultureInfo.InvariantCulture));
        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("NoProofOfDelivery");
    }

    [Fact]
    public async Task OrderWhoseRatingFailed_ShowsNotRatedBesidesTheMissingProofOfDelivery_AndEachReasonStandsOnItsOwn()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        var booked = await tms.BookedAsync(number, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        (await tms.BlockingReasonsAsync(number, _cancellationToken)).Should().Equal("NotRated", "NoProofOfDelivery");

        var proofCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, proofCorrelationId, _cancellationToken);
        await tms.PublishedAsync(proofCorrelationId, "ProofOfDeliveryReceived", _cancellationToken);

        (await tms.BlockingReasonsAsync(number, _cancellationToken)).Should().Equal("NotRated");
    }

    [Fact]
    public async Task OrderWithAnOpenException_ShowsOpenException_UntilItIsResolved_AndThenLeavesTheListAsBillable()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);

        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("OpenException");

        var open = await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken);
        var resolved = await tms.ResolvedAsync(open.Single()["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);
        await tms.PublishedAsync(resolved["correlationId"]!.GetValue<string>(), "TransportOrderBillable", _cancellationToken);

        (await tms.NotYetBillableEntryAsync(order, _cancellationToken)).Should().BeNull("a Billable order leaves the list");
    }

    [Fact]
    public async Task OrdersThatAreNotDelivered_OrBillable_OrFulfilment_AreNotListed()
    {
        var undelivered = await RatedOrderAsync();
        await tms.BookedAsync(undelivered, _cancellationToken);

        var billable = await RatedOrderAsync();
        var billableBooked = await tms.BookedAsync(billable, _cancellationToken);
        await tms.DeliveredAsync(billableBooked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.ProofRecordedAsync(billableBooked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForBillableAsync(billable, _cancellationToken);

        using var wms = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: TmsFixture.WmsClientId));
        var fulfilment = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken, wms);
        var fulfilmentBooked = await tms.BookedAsync(fulfilment, _cancellationToken);
        await tms.DeliveredAsync(fulfilmentBooked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var listed = (await tms.ListNotYetBillableAsync(_cancellationToken)).Select(entry => entry!["transportOrderNumber"]!.GetValue<string>());
        listed.Should().NotContain([undelivered, billable, fulfilment]);
    }

    [Fact]
    public async Task List_PutsTheOldestDeliveryFirst()
    {
        var first = await RatedOrderAsync();
        var second = await RatedOrderAsync();
        var firstBooked = await tms.BookedAsync(first, _cancellationToken);
        var secondBooked = await tms.BookedAsync(second, _cancellationToken);
        await tms.DeliveredAsync(secondBooked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), _cancellationToken);
        await tms.DeliveredAsync(firstBooked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var listed = (await tms.ListNotYetBillableAsync(_cancellationToken)).Select(entry => entry!["transportOrderNumber"]!.GetValue<string>()).ToList();

        listed.IndexOf(second).Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(listed.IndexOf(first), "the second order was delivered first");
    }

    private async Task<string> RatedOrderAsync()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        return number;
    }
}
