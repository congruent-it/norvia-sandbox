using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class PlanningDayConfiguration : IEntityTypeConfiguration<PlanningDay>
{
    public void Configure(EntityTypeBuilder<PlanningDay> builder)
    {
        builder.ToTable("PlanningDays");
        builder.Ignore(day => day.DomainEvents);

        // A day is identified by its date, so the database itself keeps two horizon jobs from opening a day twice.
        builder.Ignore(day => day.Id);
        builder.HasKey(day => day.Date);
        builder.Property(day => day.Date).ValueGeneratedNever();

        builder.Property(day => day.Status).HasConversion<string>().HasMaxLength(10);

        builder.Property(day => day.Shifts)
            .HasJsonList(
                shift => new StoredShift(shift.DriverId.Value, shift.Start, shift.End, shift.EditedByHand),
                stored => new Shift(new DriverId(stored.DriverId), stored.Start, stored.End, stored.EditedByHand));
        builder.Property(day => day.AvailableVehicles).HasJsonList(id => id.Value, value => new VehicleId(value));

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }

    private sealed record StoredShift(string DriverId, TimeOnly Start, TimeOnly End, bool EditedByHand);
}
