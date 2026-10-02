using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How a booking request is stored, checked on the model alone: no database is involved.</summary>
public sealed class CarrierBookingRequestConfigurationTests
{
    private static readonly string[] UniqueKey = ["PersonaId", "CarrierBookingNumber"];

    private static PartnerPortalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=unused").Options);

    [Fact]
    public void Details_AreReadBackAsTheyWereStored()
    {
        var details = new CarrierBookingDetails(
            new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", new DateTime(2030, 10, 2, 7, 30, 0), new DateTime(2030, 10, 2, 11, 0, 0)),
            new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null),
            "Getränke Schmitz GmbH",
            [new CarrierBookingGoodsLine("pallet", 4, 2400m, 120m, 80m, 160m), new CarrierBookingGoodsLine("parcel", 3, 12m, null, null, null)]);
        using var context = NewContext();
        var property = context.Model.FindEntityType(typeof(CarrierBookingRequest))!.FindProperty(nameof(CarrierBookingRequest.Details))!;
        var converter = property.GetValueConverter()!;

        var read = (CarrierBookingDetails)converter.ConvertFromProvider(converter.ConvertToProvider(details))!;

        read.Collection.Should().Be(details.Collection);
        read.Delivery.Should().Be(details.Delivery);
        read.ConsigneeName.Should().Be(details.ConsigneeName);
        read.Goods.Should().Equal(details.Goods);
    }

    [Fact]
    public void Details_AreComparedAndCopiedByWhatTheyHold()
    {
        var stop = new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null);
        var details = new CarrierBookingDetails(stop, stop, "A", [new CarrierBookingGoodsLine("pallet", 1, 1m, null, null, null)]);
        var same = new CarrierBookingDetails(stop, stop, "A", [new CarrierBookingGoodsLine("pallet", 1, 1m, null, null, null)]);
        var other = details with { ConsigneeName = "B" };
        using var context = NewContext();
        var comparer = context.Model.FindEntityType(typeof(CarrierBookingRequest))!.FindProperty(nameof(CarrierBookingRequest.Details))!.GetValueComparer()!;

        comparer.Equals(details, same).Should().BeTrue();
        comparer.Equals(details, other).Should().BeFalse();
        comparer.GetHashCode(details).Should().Be(comparer.GetHashCode(same));
        var snapshot = (CarrierBookingDetails)comparer.Snapshot(details)!;
        snapshot.Should().NotBeSameAs(details);
        comparer.Equals(details, snapshot).Should().BeTrue();
    }

    [Fact]
    public void CarrierBookingNumber_IsUniquePerPersona_SoARepeatedRequestIsStoredOnce()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(UniqueKey));
    }
}
