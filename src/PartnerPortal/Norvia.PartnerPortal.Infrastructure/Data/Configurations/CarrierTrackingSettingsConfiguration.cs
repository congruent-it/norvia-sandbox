using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class CarrierTrackingSettingsConfiguration : IEntityTypeConfiguration<CarrierTrackingSettings>
{
    public void Configure(EntityTypeBuilder<CarrierTrackingSettings> builder)
    {
        builder.ToTable("CarrierTrackingSettings");
        builder.Ignore(settings => settings.DomainEvents);

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id)
            .HasConversion(id => id.Value, value => new CarrierTrackingSettingsId(value))
            .ValueGeneratedNever();

        builder.Property(settings => settings.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(settings => settings.PersonaId).IsUnique();

        // The policy is a value: its two parts are columns, the words of the enum readable in the table.
        builder.ComplexProperty(settings => settings.Policy, policy =>
        {
            policy.Property(value => value.Mode).HasColumnName("Mode").HasConversion<string>().HasMaxLength(30).IsRequired();
            policy.Property(value => value.Delay).HasColumnName("DelayMilliseconds").HasConversion(delay => (long)delay.TotalMilliseconds, value => TimeSpan.FromMilliseconds(value)).IsRequired();
        });
    }
}
