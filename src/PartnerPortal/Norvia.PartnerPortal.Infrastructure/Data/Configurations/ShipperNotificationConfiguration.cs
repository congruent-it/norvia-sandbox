using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

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

        builder.Property(notification => notification.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(notification => notification.CorrelationId).HasMaxLength(200).IsRequired();

        // The notification ID is the key of a Persona's inbox, so a notification that arrives twice is stored once.
        builder.HasIndex(notification => new { notification.PersonaId, notification.NotificationId }).IsUnique();
        builder.HasIndex(notification => new { notification.PersonaId, notification.ReceivedAt });

        builder.ComplexProperty(notification => notification.Content, content =>
        {
            content.Property(value => value.BookingRef).HasMaxLength(200).IsRequired();
            content.Property(value => value.TransportOrderNumber).HasMaxLength(50).IsRequired();
            content.Property(value => value.ExceptionCode).HasConversion<string>().HasMaxLength(30).IsRequired();
            content.Property(value => value.Message).HasMaxLength(4000).IsRequired();
            content.Property(value => value.Eta).HasMaxLength(20);
            content.Property(value => value.DeliverFrom).HasMaxLength(20);
            content.Property(value => value.DeliverUntil).HasMaxLength(20);
            content.Property(value => value.Contact).HasMaxLength(200).IsRequired();
        });
    }
}
