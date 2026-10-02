using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class CarrierAnswerSettingsConfiguration : IEntityTypeConfiguration<CarrierAnswerSettings>
{
    public void Configure(EntityTypeBuilder<CarrierAnswerSettings> builder)
    {
        builder.ToTable("CarrierAnswerSettings");
        builder.Ignore(settings => settings.DomainEvents);

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id)
            .HasConversion(id => id.Value, value => new CarrierAnswerSettingsId(value))
            .ValueGeneratedNever();

        builder.Property(settings => settings.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(settings => settings.PersonaId).IsUnique();

        // The policy is a value: its three parts are columns, the words of the enums readable in the table.
        builder.ComplexProperty(settings => settings.Policy, policy =>
        {
            policy.Property(value => value.Mode).HasColumnName("Mode").HasConversion<string>().HasMaxLength(30).IsRequired();
            policy.Property(value => value.RejectionReason).HasColumnName("RejectionReason").HasConversion<string>().HasMaxLength(30).IsRequired();
            policy.Property(value => value.Delay).HasColumnName("DelayMilliseconds").HasConversion(delay => (long)delay.TotalMilliseconds, value => TimeSpan.FromMilliseconds(value)).IsRequired();
        });
    }
}
