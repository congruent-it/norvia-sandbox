using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Carriers;

public sealed class CarrierReferencesTests
{
    private static readonly SimulatedCarrier Carrier = new("CAR-0001", "Van Dijk Transport", true, "van-dijk-transport", "Van Dijk Transport", "VDT");

    [Fact]
    public void BookingReference_StartsWithTheCarriersPrefixAndNamesTheRunAndTheOrder()
    {
        CarrierReferences.BookingReference(Carrier, runNumber: 12, step: 7).Should().Be("VDT-SIM12-7");
    }

    [Fact]
    public void EventId_IsUniqueForEachEventOfAShipment()
    {
        var ids = Enum.GetValues<CarrierEvent>().Select(carrierEvent => CarrierReferences.EventId(Carrier, 12, 7, carrierEvent)).ToList();

        ids.Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.StartsWith("VDT-SIM12-7-", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentId_NamesTheProofOfTheShipment()
    {
        CarrierReferences.DocumentId(Carrier, 12, 7).Should().Be("VDT-SIM12-7-POD");
    }

    [Fact]
    public void StatusCode_IsTheSharedCodeOfEachEvent()
    {
        CarrierReferences.StatusCode(CarrierEvent.Collected).Should().Be("PU");
        CarrierReferences.StatusCode(CarrierEvent.Departed).Should().Be("DEP");
        CarrierReferences.StatusCode(CarrierEvent.Arrived).Should().Be("ARR");
        CarrierReferences.StatusCode(CarrierEvent.OutForDelivery).Should().Be("OFD");
        CarrierReferences.StatusCode(CarrierEvent.Delivered).Should().Be("POD");
        CarrierReferences.StatusCode(CarrierEvent.CollectionFailed).Should().Be("NPU");
        CarrierReferences.StatusCode(CarrierEvent.DeliveryFailed).Should().Be("NDL");
        CarrierReferences.StatusCode(CarrierEvent.Damaged).Should().Be("DMG");
    }

    [Fact]
    public void ABookingReference_OfACarrierWithoutAPrefix_IsRefused()
    {
        var act = () => CarrierReferences.BookingReference(new SimulatedCarrier("CAR-0009", "Nobody", true), 1, 1);

        act.Should().Throw<InvalidOperationException>().WithMessage("*CAR-0009*");
    }
}
