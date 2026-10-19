using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class BillableLineConfiguration : IEntityTypeConfiguration<BillableLine>
{
    public void Configure(EntityTypeBuilder<BillableLine> builder)
    {
        builder.ToTable("BillableLines");
        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.Total);
        // The Transport Order number is the key, so two recordings of one Transport Order can't both be stored.
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id)
            .HasConversion(id => id.TransportOrderNumber, value => new BillableLineId(value))
            .HasMaxLength(20).ValueGeneratedNever();
        builder.Property(line => line.ShipperId).HasConversion(id => id.Value, value => new ShipperId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Shipper>().WithMany().HasForeignKey(line => line.ShipperId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(line => line.ShipperReference).HasMaxLength(200).IsRequired();
        builder.Property(line => line.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(line => line.FreightCharge).AsJsonDocument();
        builder.Property(line => line.IntakeCorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(line => line.BillingCorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(line => line.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(line => line.InvoiceNumber)
            .HasConversion(number => number!.Value.Value, value => new InvoiceNumber(value))
            .HasMaxLength(20);
        // Two Invoice runs that take the same Waiting line conflict on the row version, so a line is invoiced once and only once.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
        // An Invoice run takes a Shipper's Waiting lines.
        builder.HasIndex(line => new { line.ShipperId, line.Status });
    }
}
