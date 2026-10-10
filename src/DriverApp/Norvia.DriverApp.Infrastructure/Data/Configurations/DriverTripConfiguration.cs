using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Infrastructure.Data.Configurations;

internal sealed class DriverTripConfiguration : IEntityTypeConfiguration<DriverTrip>
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<DriverTrip> builder)
    {
        builder.ToTable("DriverTrips");
        builder.HasKey(trip => trip.Id);
        builder.Property(trip => trip.Id).HasColumnName("TripNumber").HasMaxLength(16).ValueGeneratedNever()
            .HasConversion(number => number.Value, value => new TripNumber(value));

        // Two versions of one Trip racing each other: the one that finds the Version changed gets a conflict, so a lower
        // version can never overwrite a higher one.
        builder.Property(trip => trip.Version).IsConcurrencyToken();
        builder.Property(trip => trip.DriverId).HasMaxLength(32).HasConversion(id => id.Value, value => new DriverId(value));
        builder.HasIndex(trip => trip.DriverId);

        // Everything else a version says is replaced whole and read whole, so it is one JSON document.
        builder.Property(trip => trip.Details).HasColumnType("nvarchar(max)").HasConversion(
            details => JsonSerializer.Serialize(details, DetailsJson),
            json => JsonSerializer.Deserialize<TripDetails>(json, DetailsJson)!);
    }
}
