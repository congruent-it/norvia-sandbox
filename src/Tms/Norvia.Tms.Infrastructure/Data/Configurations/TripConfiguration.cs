using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public const string VehicleDayIndex = "IX_Trips_Day_VehicleId";
    public const string DriverDayIndex = "IX_Trips_Day_DriverId";
    public const string IdempotencyKeyIndex = "IX_Trips_IdempotencyKey";

    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");
        builder.Ignore(trip => trip.DomainEvents);

        builder.HasKey(trip => trip.Id);
        builder.Property(trip => trip.Id).HasConversion(id => id.Value, value => new TripId(value)).ValueGeneratedNever();

        builder.Property(trip => trip.Number).HasConversion(number => number.Value, value => TripNumber.Parse(value)).HasMaxLength(10).IsRequired();
        builder.Property(trip => trip.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(trip => trip.VehicleId).HasConversion(id => id!.Value, value => new VehicleId(value)).HasMaxLength(20);
        builder.Property(trip => trip.DriverId).HasConversion(id => id!.Value, value => new DriverId(value)).HasMaxLength(20);

        builder.Ignore(trip => trip.PlannedDriving);

        builder.Property(trip => trip.IdempotencyKey).HasConversion(key => key.Value, value => new IdempotencyKey(value)).HasMaxLength(IdempotencyKey.MaxLength).IsRequired();

        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasIndex(trip => trip.Number).IsUnique();
        builder.HasIndex(trip => trip.IdempotencyKey, IdempotencyKeyIndex).IsUnique();
        builder.HasIndex(trip => trip.Day);

        // A Vehicle or Driver has at most one Trip a day, Drafts included. The handlers check it; this keeps two
        // Planners who ask at once from both getting it.
        builder.HasIndex(trip => new { trip.Day, trip.VehicleId }, VehicleDayIndex).IsUnique().HasFilter("[VehicleId] IS NOT NULL");
        builder.HasIndex(trip => new { trip.Day, trip.DriverId }, DriverDayIndex).IsUnique().HasFilter("[DriverId] IS NOT NULL");
    }
}
