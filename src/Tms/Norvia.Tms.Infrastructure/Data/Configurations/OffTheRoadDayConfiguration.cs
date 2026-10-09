using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class OffTheRoadDayConfiguration : IEntityTypeConfiguration<OffTheRoadDay>
{
    public void Configure(EntityTypeBuilder<OffTheRoadDay> builder)
    {
        builder.ToTable("OffTheRoadDays");
        builder.Ignore(day => day.DomainEvents);

        builder.HasKey(day => day.Id);
        builder.Property(day => day.Id).HasConversion(id => id.Value, value => new OffTheRoadDayId(value)).ValueGeneratedNever();

        builder.Property(day => day.VehicleId).HasConversion(id => id.Value, value => new VehicleId(value)).HasMaxLength(20).IsRequired();
        builder.Property(day => day.Reason).HasConversion<string>().HasMaxLength(12);
        builder.Property(day => day.Remark).HasMaxLength(AvailabilityPeriod.MaxRemarkLength).IsRequired();

        builder.HasIndex(day => new { day.VehicleId, day.Date }).IsUnique();
        builder.HasIndex(day => day.Date);
    }
}
