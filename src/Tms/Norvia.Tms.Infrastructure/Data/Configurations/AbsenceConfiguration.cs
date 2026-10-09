using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> builder)
    {
        builder.ToTable("Absences");
        builder.Ignore(absence => absence.DomainEvents);

        builder.HasKey(absence => absence.Id);
        builder.Property(absence => absence.Id).HasConversion(id => id.Value, value => new AbsenceId(value)).ValueGeneratedNever();

        builder.Property(absence => absence.DriverId).HasConversion(id => id.Value, value => new DriverId(value)).HasMaxLength(20).IsRequired();
        builder.Property(absence => absence.Reason).HasConversion<string>().HasMaxLength(10);
        builder.Property(absence => absence.Remark).HasMaxLength(Absence.MaxRemarkLength).IsRequired();

        // A Driver is absent a day or isn't: recording it twice is one record.
        builder.HasIndex(absence => new { absence.DriverId, absence.Date }).IsUnique();
        builder.HasIndex(absence => absence.Date);
    }
}
