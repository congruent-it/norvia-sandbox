using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class ShipperNotificationConfiguration : IEntityTypeConfiguration<ShipperNotification>
{
    public void Configure(EntityTypeBuilder<ShipperNotification> builder)
    {
        builder.ToTable("ShipperNotifications");
        builder.Ignore(notification => notification.DomainEvents);

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id)
            .HasConversion(id => id.Value, value => new ShipperNotificationId(value))
            .ValueGeneratedNever();

        // An Exception's notifications: any number over its life, never deleted with it.
        builder.Property(notification => notification.ExceptionId)
            .HasConversion(id => id.Value, value => new ExceptionId(value));
        builder.HasOne<ExceptionCase>().WithMany().HasForeignKey(notification => notification.ExceptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(notification => new { notification.ExceptionId, notification.RequestedAt });

        builder.Property(notification => notification.ExceptionNumber)
            .HasConversion(number => number.Value, value => ExceptionNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(notification => notification.ShipmentNumber)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(notification => notification.TransportOrderNumber)
            .HasConversion(number => number.Value, value => TransportOrderNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(notification => notification.ShipperId)
            .HasConversion(id => id.Value, value => new ShipperId(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(notification => notification.ShipperReference).HasMaxLength(TransportOrderRules.MaxShipperReferenceLength);
        builder.Property(notification => notification.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(notification => notification.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(notification => notification.Message).HasMaxLength(ShipperNotification.MaxMessageLength).IsRequired();
        builder.Property(notification => notification.Planner).HasMaxLength(ShipperNotification.MaxPlannerLength).IsRequired();
        builder.Property(notification => notification.Eta);

        builder.Property(notification => notification.AgreedDeliveryEarliest);
        builder.Property(notification => notification.AgreedDeliveryLatest);

        builder.Property(notification => notification.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(notification => notification.RequestedAt).IsRequired();
        builder.Property(notification => notification.SentAt);
        builder.Property(notification => notification.NotDeliveredAt);
        builder.Property(notification => notification.NotDeliveredStatusCode);
        builder.Property(notification => notification.NotDeliveredMessage).HasMaxLength(ShipperNotification.MaxCauseLength);

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
