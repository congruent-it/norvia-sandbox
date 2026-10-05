using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How a booking request is stored, checked on the model alone: no database is involved.</summary>
public sealed class CarrierBookingRequestConfigurationTests
{
    private static readonly string[] UniqueKey = ["PersonaId", "CarrierBookingNumber"];
    private static readonly string[] DueTime = ["AutoAnswerDueAt"];
    private static readonly string[] AutoProgressDueTime = ["AutoProgressDueAt"];
    private static readonly string[] PersonaKey = ["PersonaId"];

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
    public void LastProgress_IsStoredAsItsName_AndTheStageIsDerivedNotStored()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.FindProperty(nameof(CarrierBookingRequest.LastProgress))!.GetProviderClrType().Should().Be<string>();
        entity.FindProperty(nameof(CarrierBookingRequest.Stage)).Should().BeNull();
    }

    [Fact]
    public void CarrierBookingNumber_IsUniquePerPersona_SoARepeatedRequestIsStoredOnce()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(UniqueKey));
    }

    [Fact]
    public void StatusAndRejectionReason_AreStoredAsTheirNames_SoTheTableStaysReadable()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.FindProperty(nameof(CarrierBookingRequest.Status))!.GetProviderClrType().Should().Be<string>();
        entity.FindProperty(nameof(CarrierBookingRequest.RejectionReason))!.GetProviderClrType().Should().Be<string>();
    }

    [Fact]
    public void TheAnswersColumns_AreOptional_ExceptTheStatus()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.FindProperty(nameof(CarrierBookingRequest.Status))!.IsNullable.Should().BeFalse();
        foreach (var name in new[] { "ClosedAt", "AnswerCorrelationId", "CarrierBookingReference", "PlannedCollection", "RejectionReason", "RejectionText" })
        {
            entity.FindProperty(name)!.IsNullable.Should().BeTrue(name);
        }
    }

    [Fact]
    public void TheAutomaticAnswer_IsStoredAsWordsAndDates_AndTheDueTimeIsIndexed()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;

        entity.FindProperty(nameof(CarrierBookingRequest.AutoAnswerMode))!.GetProviderClrType().Should().Be<string>();
        entity.FindProperty(nameof(CarrierBookingRequest.AutoAnswerReason))!.GetProviderClrType().Should().Be<string>();
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(DueTime));
    }

    [Fact]
    public void ThePolicyOfACarrierPersona_IsStoredOncePerPersona_AsColumnsOfItsOwn()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierAnswerSettings))!;

        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(PersonaKey));
        var policy = entity.FindComplexProperty(nameof(CarrierAnswerSettings.Policy))!.ComplexType;
        policy.GetProperties().Select(property => property.GetColumnName()).Should().BeEquivalentTo("Mode", "RejectionReason", "DelayMilliseconds");
    }

    [Fact]
    public void TheDelay_IsStoredInMilliseconds()
    {
        using var context = NewContext();
        var delay = context.Model.FindEntityType(typeof(CarrierAnswerSettings))!
            .FindComplexProperty(nameof(CarrierAnswerSettings.Policy))!.ComplexType.FindProperty(nameof(CarrierAnswerPolicy.Delay))!;
        var converter = delay.GetValueConverter()!;

        converter.ConvertToProvider(TimeSpan.FromSeconds(7)).Should().Be(7000L);
        converter.ConvertFromProvider(7000L).Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void TheAutomaticTrackingEvents_AreStoredAsMillisecondsAndADate_AndTheDueTimeIsIndexed()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierBookingRequest))!;
        var converter = entity.FindProperty(nameof(CarrierBookingRequest.AutoProgressDelay))!.GetValueConverter()!;

        converter.ConvertToProvider(TimeSpan.FromSeconds(7)).Should().Be(7000L);
        converter.ConvertToProvider(null).Should().BeNull();
        converter.ConvertFromProvider(7000L).Should().Be(TimeSpan.FromSeconds(7));
        converter.ConvertFromProvider(null).Should().BeNull();
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(AutoProgressDueTime));
    }

    [Fact]
    public void TheTrackingPolicyOfACarrierPersona_IsStoredOncePerPersona_AsColumnsOfItsOwn()
    {
        using var context = NewContext();
        var entity = context.Model.FindEntityType(typeof(CarrierTrackingSettings))!;

        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(PersonaKey));
        var policy = entity.FindComplexProperty(nameof(CarrierTrackingSettings.Policy))!.ComplexType;
        policy.GetProperties().Select(property => property.GetColumnName()).Should().BeEquivalentTo("Mode", "DelayMilliseconds");
        policy.FindProperty(nameof(CarrierTrackingPolicy.Mode))!.GetProviderClrType().Should().Be<string>();
        var converter = policy.FindProperty(nameof(CarrierTrackingPolicy.Delay))!.GetValueConverter()!;
        converter.ConvertToProvider(TimeSpan.FromSeconds(7)).Should().Be(7000L);
        converter.ConvertFromProvider(7000L).Should().Be(TimeSpan.FromSeconds(7));
    }
}
