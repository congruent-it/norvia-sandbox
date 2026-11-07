using System.Text.Json;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The reply agent's view is what the Shipper sees of an order and nothing more (ADR 0011, #426).</summary>
public sealed class CustomerServiceReplyAgentViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static ShipperView View(bool delivered) => new(
        new ShipperTransportOrderDetail(
            "TO-0000042",
            "BD-1",
            new ShipperPlaceView("Rotterdam", "NL"),
            new ShipperPlaceView("Utrecht", "NL"),
            new ShipperWindowView(Now.AddDays(-2), Now.AddDays(-1)),
            new ShipperWindowView(Now, Now.AddHours(4)),
            delivered ? TrackingStatus.Delivered : TrackingStatus.InTransit,
            Now.AddHours(3),
            Now.AddHours(-1),
            [new ShipperProgressEntry("Departed", Now.AddHours(-1), new ShipperPlaceView("Venlo", "DE"))],
            delivered
                ? new ShipperProofOfDelivery("Sanne de Vries", Now, DeliveryCondition.Reservations, [new ShipperProofOfDeliveryFile(Guid.NewGuid(), "cmr-signed.pdf", "application/pdf", 1234)])
                : null),
        [new ShipperNotificationSent(Now.AddHours(-2), "Your shipment is delayed.")]);

    [Fact]
    public void Of_IsTheTrackingStatusProgressProofAvailabilityAndNotifications()
    {
        var view = CustomerServiceReplyAgentMapping.Of(View(delivered: true), Now);

        view.TrackingStatus.Should().Be("Delivered");
        view.AsOf.Should().Be(Now);
        view.Progress.Should().ContainSingle().Which.Place.City.Should().Be("Venlo");
        view.ProofOfDelivery.Should().Be(new ReplyAgentProofOfDeliveryView(true, Now, "Reservations"));
        view.Notifications.Should().ContainSingle().Which.Message.Should().Be("Your shipment is delayed.");
    }

    [Fact]
    public void Of_NoProofOfDelivery_IsNotAvailable()
    {
        CustomerServiceReplyAgentMapping.Of(View(delivered: false), Now).ProofOfDelivery.Should().Be(new ReplyAgentProofOfDeliveryView(false, null, null));
    }

    [Fact]
    public void Of_NeverCarriesWhoSignedAFileOrAnythingTheShipperDoesNotSee()
    {
        var json = JsonSerializer.Serialize(CustomerServiceReplyAgentMapping.Of(View(delivered: true), Now));

        json.Should().NotContainAny("Sanne", "cmr-signed", "application/pdf", "files", "receivedBy");
        json.Should().NotContainAny("shipmentNumber", "carrier", "Carrier", "exception", "Exception", "stale", "Stale", "remark", "price", "Price");
    }

    [Fact]
    public void TheView_IsAnAllowlist_SoANewFieldIsExcludedUntilSomeoneAddsItHere()
    {
        var names = typeof(ReplyAgentTrackingView).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ReplyAgentTrackingView).Namespace && type.Name.StartsWith("ReplyAgent", StringComparison.Ordinal) && type.Name.EndsWith("View", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToList();

        names.Should().BeEquivalentTo(
            [
                "TransportOrderNumber", "ShipperReference", "TrackingStatus", "AsOf", "Collection", "Delivery", "DeliveryWindow", "Eta", "LastUpdate", "Progress", "ProofOfDelivery", "Notifications",
                "City", "Country",
                "Earliest", "Latest",
                "Type", "EventTime", "Place",
                "Available", "DeliveryTime", "Condition",
                "SentAt", "Message",
            ]);
    }
}
