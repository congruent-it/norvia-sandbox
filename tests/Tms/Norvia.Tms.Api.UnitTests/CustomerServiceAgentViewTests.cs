using System.Reflection;
using System.Text.Json;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The Customer service agent's view of a Rejected booking is narrower than the read models (ADR 0040, #421).</summary>
public sealed class CustomerServiceAgentViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    private static TransportOrderView Booking(params string[] codes) => new(
        Guid.Parse("3f2b8c9e-6d5a-4c1e-9a77-0e1f2a3b4c5d"),
        null,
        TransportOrderStatus.Rejected,
        null,
        new ShipperView("SHP-0001", "Brouwer Dranken"),
        IntakeChannel.ShipperApi,
        "corr-2",
        Now,
        [.. codes.Select(code => new RejectionReasonView(code, $"{code} message"))],
        null,
        null,
        null,
        new CanonicalTransportOrder(
            null,
            "SHP-0001",
            "BD-2026-0001",
            new CanonicalCollectionDetails(
                new CanonicalAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
                new CanonicalTimeWindow(Now.AddDays(1), Now.AddDays(1).AddHours(4))),
            new CanonicalDeliveryDetails(
                new CanonicalAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
                new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", "sanne@dezwaan.example"),
                new CanonicalTimeWindow(Now.AddDays(1).AddHours(6), Now.AddDays(1).AddHours(10))),
            [new CanonicalGoodsLine("pallet", 2, -850m, new CanonicalDimensions(120m, 80m, 150m)), new CanonicalGoodsLine("crate", null, 60.5m, null)],
            "Ring at the side entrance and ask for Sanne"));

    [Fact]
    public void Of_NeverCarriesAConsigneeNameAddressOrContact_NorTheShippersFreeText()
    {
        var json = JsonSerializer.Serialize(CustomerServiceAgentMapping.Of(Booking("WeightInvalid")));

        json.Should().NotContainAny("Café De Zwaan", "Sanne", "+31 30 555 0142", "dezwaan", "Oudegracht", "3511 AW", "Utrecht", "Ring at the side entrance");
        json.Should().NotContainAny("Waalhaven Oostzijde", "consignee", "Consignee", "instructions", "Instructions");
    }

    [Fact]
    public void Of_CarriesTheStatusesCodesTimesNumbersAndBusinessNames()
    {
        var view = CustomerServiceAgentMapping.Of(Booking("WeightInvalid", "GoodsLinesMissing"));

        view.Status.Should().Be("Rejected");
        view.Channel.Should().Be("ShipperApi");
        view.CorrelationId.Should().Be("corr-2");
        view.ReceivedAt.Should().Be(Now);
        view.Shipper.Should().Be(new AgentShipperView("SHP-0001", "Brouwer Dranken"));
        view.RejectionReasons.Should().Equal(
            new AgentRejectionReasonView("WeightInvalid", "WeightInvalid message", "goodsLines[].weightKg"),
            new AgentRejectionReasonView("GoodsLinesMissing", "GoodsLinesMissing message", "goodsLines"));
        view.Submitted.ShipperReference.Should().Be("BD-2026-0001");
        view.Submitted.Collection.Should().Be(new AgentCollectionView("3087 BM", "Rotterdam", "NL", Now.AddDays(1), Now.AddDays(1).AddHours(4)));
        view.Submitted.Delivery.Should().Be(new AgentDeliveryView(Now.AddDays(1).AddHours(6), Now.AddDays(1).AddHours(10)));
        view.Submitted.GoodsLines.Should().Equal(
            new AgentGoodsLineView("pallet", 2, -850m, new AgentDimensionsView(120m, 80m, 150m)),
            new AgentGoodsLineView("crate", null, 60.5m, null));
    }

    [Fact]
    public void Of_ABookingWithNoCollectionDeliveryOrGoods_IsTheShapeOfWhatWasGiven()
    {
        var booking = Booking() with { TransportOrder = new CanonicalTransportOrder(null, "SHP-0001", null, null, null, null, null) };

        var view = CustomerServiceAgentMapping.Of(booking);

        view.Submitted.Should().BeEquivalentTo(new AgentSubmittedBookingView(null, null, null, []));
        view.RejectionReasons.Should().BeEmpty();
    }

    [Fact]
    public void TheView_IsAnAllowlist_SoANewFieldIsExcludedUntilSomeoneAddsItHere()
    {
        var names = AgentViewTypes().SelectMany(type => type.GetProperties()).Select(property => property.Name).ToList();

        names.Should().BeEquivalentTo(
            [
                "Id", "Status", "ReceivedAt", "CorrelationId", "Channel", "Shipper", "RejectionReasons", "Submitted",
                "Id", "Name",
                "Code", "Message", "Field",
                "ShipperReference", "Collection", "Delivery", "GoodsLines",
                "Postcode", "City", "Country", "Earliest", "Latest",
                "Earliest", "Latest",
                "PackageType", "Quantity", "WeightKg", "DimensionsCm",
                "Length", "Width", "Height",
                "TransportOrderNumber", "Status", "ReceivedAt", "CorrelationId", "Channel", "Shipper", "ShipperReference", "ShipmentNumber", "ShipmentStatus", "Rating", "Billing",
                "Result", "FailureCode", "FailureMessage",
                "State", "BecameBillableAt", "ReleasedWithoutProofOfDelivery", "BlockingReasons",
                "ShipmentNumber", "TrackingEvents",
                "Type", "EventTime", "Place", "CountryCode", "Eta", "ReceivedAt", "MovedShipment", "Source",
                "ShipmentNumber", "Exceptions",
                "ExceptionNumber", "Type", "Status", "OpenedAt", "AfterDelivered", "Causes", "Notes", "ShipperNotified", "ShipperNotifiedAt", "Outcome", "ResolvedAt",
                "Text", "AddedAt",
                "ShipmentNumber", "Status", "WithReservations", "ProofsOfDelivery",
                "Source", "DeliveryTime", "Condition", "IsCurrent", "ReceivedAt", "FileCount",
                "TransportOrderNumber", "Eligible", "ShipmentNumber", "DeliveredAt", "AsOf", "BlockingReasons",
            ]);
        names.Should().NotContain(name => name.Contains("Consignee", StringComparison.Ordinal) || name.Contains("Street", StringComparison.Ordinal)
            || name.Contains("Phone", StringComparison.Ordinal) || name.Contains("Email", StringComparison.Ordinal) || name.Contains("Contact", StringComparison.Ordinal)
            || name.Contains("Instructions", StringComparison.Ordinal) || name.Contains("Content", StringComparison.Ordinal) || name.Contains("Remark", StringComparison.Ordinal)
            || name.Contains("ReceivedBy", StringComparison.Ordinal) || name.Contains("Signed", StringComparison.Ordinal) || name.Contains("Message", StringComparison.Ordinal) && name != "FailureMessage" && name != "Message"
            || name.Contains("File", StringComparison.Ordinal) && name != "FileCount");
    }

    [Fact]
    public void FieldOf_EveryRejectionCode_NamesTheSubmittedFieldItIsAbout()
    {
        Enum.GetNames<RejectionCode>().Select(CustomerServiceAgentMapping.FieldOf).Should().NotContain("unknown");
        CustomerServiceAgentMapping.FieldOf("NotACode").Should().Be("unknown");
    }

    private static IEnumerable<Type> AgentViewTypes() =>
        typeof(AgentRejectedBookingView).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AgentRejectedBookingView).Namespace && type.Name.StartsWith("Agent", StringComparison.Ordinal) && type.Name.EndsWith("View", StringComparison.Ordinal)
                && type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null);
}
