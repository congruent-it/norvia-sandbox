using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Domain.ShipperNotifications;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How a Shipper notification is stored, checked on the model alone: no database is involved.</summary>
public sealed class ShipperNotificationConfigurationTests
{
    private static readonly string[] UniqueKey = ["PersonaId", "NotificationId"];

    private static PartnerPortalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=unused").Options);

    [Fact]
    public void NotificationId_IsUniquePerPersona_SoARepeatedNotificationIsStoredOnce()
    {
        using var context = NewContext();

        context.Model.FindEntityType(typeof(ShipperNotification))!.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(UniqueKey));
    }

    [Fact]
    public void ExceptionCode_IsStoredAsItsName()
    {
        using var context = NewContext();

        var property = context.Model.FindEntityType(typeof(ShipperNotification))!
            .FindComplexProperty(nameof(ShipperNotification.Content))!.ComplexType.FindProperty(nameof(ShipperNotificationContent.ExceptionCode))!;

        property.GetProviderClrType().Should().Be<string>();
    }

    [Theory]
    [InlineData(nameof(DeliveryTiming.Eta))]
    [InlineData(nameof(DeliveryTiming.DeliverFrom))]
    [InlineData(nameof(DeliveryTiming.DeliverUntil))]
    public void Timing_IsStoredAsOptionalDateTimes(string name)
    {
        using var context = NewContext();

        var property = context.Model.FindEntityType(typeof(ShipperNotification))!
            .FindComplexProperty(nameof(ShipperNotification.Content))!.ComplexType
            .FindComplexProperty(nameof(ShipperNotificationContent.Timing))!.ComplexType.FindProperty(name)!;

        property.ClrType.Should().Be<DateTime?>();
        property.IsNullable.Should().BeTrue();
    }
}
