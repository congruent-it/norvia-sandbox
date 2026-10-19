using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CarrierCostConfiguration : IEntityTypeConfiguration<CarrierCost>
{
    public void Configure(EntityTypeBuilder<CarrierCost> builder)
    {
        builder.ToTable("CarrierCosts");
        builder.Ignore(cost => cost.DomainEvents);
        builder.Ignore(cost => cost.Total);
        // The Carrier booking number is the key, so two recordings of one Carrier booking can't both be stored.
        builder.HasKey(cost => cost.Id);
        builder.Property(cost => cost.Id)
            .HasConversion(id => id.CarrierBookingNumber, value => new CarrierCostId(value))
            .HasMaxLength(20).ValueGeneratedNever();
        builder.Property(cost => cost.CarrierBookingReference).HasMaxLength(200).IsRequired();
        builder.Property(cost => cost.CarrierId).HasConversion(id => id.Value, value => new CarrierId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Carrier>().WithMany().HasForeignKey(cost => cost.CarrierId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(cost => cost.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(cost => cost.TransportOrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(cost => cost.Charge).AsChargeDocument();
        builder.Property(cost => cost.Source).AsSourceDocument();
        builder.Property(cost => cost.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(cost => cost.Status).HasConversion<string>().HasMaxLength(20);
        // A Carrier invoice line finds its Carrier cost by Carrier and Carrier booking reference, and the Open list reads a Carrier's Open costs.
        builder.HasIndex(cost => new { cost.CarrierId, cost.CarrierBookingReference });
        builder.HasIndex(cost => new { cost.CarrierId, cost.Status });
        // Two Carrier invoices matching one Carrier cost at once conflict instead of both winning; the loser is run again and finds it Invoiced.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
