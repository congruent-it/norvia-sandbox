using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.Ignore(vehicle => vehicle.DomainEvents);
        builder.HasKey(vehicle => vehicle.Id);
        builder.Property(vehicle => vehicle.Id).HasConversion(id => id.Value, value => new VehicleId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(vehicle => vehicle.Registration).HasMaxLength(50).IsRequired();
        builder.Property(vehicle => vehicle.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(vehicle => vehicle.Revision).HasDefaultValue(1);
        builder.Property(vehicle => vehicle.FinanceOwned).HasDefaultValue(false);
        builder.Property(vehicle => vehicle.LastSaveKey).HasMaxLength(MasterDataSaveKey.MaxLength);
        // Two edits at once conflict instead of both winning; the loser is run again and sees the new revision.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
