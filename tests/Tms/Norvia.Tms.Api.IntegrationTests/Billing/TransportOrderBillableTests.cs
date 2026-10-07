using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// A Transport-only order becomes Billable when its Shipment is Delivered (P5), it is Rated (P2) and it has a current
/// Proof of delivery (P6), in whatever order those complete (P9). The TMS checks inside the transaction of each of those
/// changes and publishes one TransportOrderBillable through its outbox, on the correlation ID of the transaction that
/// completed the last condition.
/// </summary>
public sealed class TransportOrderBillableTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task RatedDeliveredOrderThenItsProofOfDelivery_BecomesBillable_AndPublishesOneTransportOrderBillable_WithEverythingTheErpNeeds()
    {
        var (order, intake) = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        var deliveredCorrelationId = TmsCalls.NewCorrelationId();
        await tms.DeliveredAsync(booked, deliveredCorrelationId, _cancellationToken);

        await tms.PublishedAsync(deliveredCorrelationId, "ShipmentDelivered", _cancellationToken);
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable", "the Shipment is Delivered, but there is no Proof of delivery yet");
        tms.BillablePublished(order).Should().BeEmpty();

        var proofCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, proofCorrelationId, _cancellationToken);

        var message = (await tms.PublishedAsync(proofCorrelationId, "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        tms.BillablePublished(order).Should().ContainSingle("the event is published once");
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");

        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTransportOrderBillable(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        var stored = await tms.GetTransportOrderAsync(order, _cancellationToken);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(order);
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        body["shipperReference"]!.GetValue<string>().Should().Be(stored["transportOrder"]!["shipperReference"]!.GetValue<string>());
        body["shipmentNumber"]!.GetValue<string>().Should().Be(booked.ShipmentNumber);
        body["releasedWithoutProofOfDelivery"]!.GetValue<bool>().Should().BeFalse();
        JsonNode.DeepEquals(body["freightCharge"], stored["freightCharge"]).Should().BeTrue("the Freight Charge is the one frozen at Rating");
        body["correlationId"]!.GetValue<string>().Should().Be(proofCorrelationId, "the Proof of delivery's transaction completed the last condition");
        body["intakeCorrelationId"]!.GetValue<string>().Should().Be(intake);
        message.CorrelationId.Should().Be(proofCorrelationId);
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["flow"] = "billing",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = order,
            ["shipmentNumber"] = booked.ShipmentNumber,
        });
        var deliveryTime = (await tms.GetTimelineAsync(booked.ShipmentNumber, _cancellationToken))["trackingEvents"]!.AsArray()
            .Single(e => e!["type"]!.GetValue<string>() == "Delivered")!["eventTime"]!.GetValue<string>();
        DateTimeOffset.Parse(body["deliveryTime"]!.GetValue<string>(), CultureInfo.InvariantCulture)
            .Should().Be(DateTimeOffset.Parse(deliveryTime, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ProofOfDeliveryThenDelivered_BecomesBillableOnTheDeliveredEventsTrail_AndPublishesOneTransportOrderBillable()
    {
        var (order, intake) = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.ProofRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(order).Should().BeEmpty("a Proof of delivery alone doesn't make an order Billable");

        var deliveredCorrelationId = TmsCalls.NewCorrelationId();
        await tms.DeliveredAsync(booked, deliveredCorrelationId, _cancellationToken);

        var message = (await tms.PublishedAsync(deliveredCorrelationId, "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        tms.BillablePublished(order).Should().ContainSingle();
        var body = JsonNode.Parse(message.Body.ToString())!;
        body["correlationId"]!.GetValue<string>().Should().Be(deliveredCorrelationId);
        body["intakeCorrelationId"]!.GetValue<string>().Should().Be(intake);
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");
    }

    [Fact]
    public async Task BillableOrder_StaysBillableAndPublishesNothingMore_WhateverHappensToItsShipmentLater()
    {
        var (order, _) = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var billableCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, billableCorrelationId, _cancellationToken);
        await tms.PublishedAsync(billableCorrelationId, "TransportOrderBillable", _cancellationToken);

        // A Proof of delivery that replaces the current one, and a Delivered event sent again under a new ID.
        await tms.ProofRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken, documentId: "replacement");
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);

        tms.BillablePublished(order).Should().ContainSingle("Billable is final");
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");
    }

    [Fact]
    public async Task DeliveredOrderWithoutAProofOfDelivery_StaysNotYetBillable_AndPublishesNothing()
    {
        var (order, _) = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.DeliveredAsync(booked, correlationId, _cancellationToken);
        await tms.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);

        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable");
        tms.BillablePublished(order).Should().BeEmpty();
    }

    [Fact]
    public async Task FulfilmentOrder_IsNotBilledSeparately_AndNeverPublishesTransportOrderBillable()
    {
        using var wms = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: TmsFixture.WmsClientId));
        var order = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken, wms);
        var booked = await tms.BookedAsync(order, _cancellationToken);

        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var proofCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, proofCorrelationId, _cancellationToken);
        await tms.PublishedAsync(proofCorrelationId, "ProofOfDeliveryReceived", _cancellationToken);

        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotBilledSeparately");
        tms.BillablePublished(order).Should().BeEmpty();
    }

    [Fact]
    public async Task NewTransportOnlyOrder_IsNotYetBillable()
    {
        var (order, _) = await RatedOrderAsync();

        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable");
    }

    private async Task<(string Number, string Intake)> RatedOrderAsync()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), intake, _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        return (number, intake);
    }
}
