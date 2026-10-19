using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class ShipperInvoiceConfiguration : IEntityTypeConfiguration<ShipperInvoice>
{
    public void Configure(EntityTypeBuilder<ShipperInvoice> builder)
    {
        builder.ToTable("ShipperInvoices");
        builder.Ignore(invoice => invoice.DomainEvents);
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id)
            .HasConversion(number => number.Value, value => new InvoiceNumber(value))
            .HasMaxLength(20).ValueGeneratedNever();
        builder.Property(invoice => invoice.ShipperId).HasConversion(id => id.Value, value => new ShipperId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Shipper>().WithMany().HasForeignKey(invoice => invoice.ShipperId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(invoice => invoice.Lines).AsJsonDocument();
        builder.Property(invoice => invoice.TotalWithoutVat).HasPrecision(18, 2);
        builder.Property(invoice => invoice.Vat).HasPrecision(18, 2);
        builder.Property(invoice => invoice.TotalWithVat).HasPrecision(18, 2);
        builder.Property(invoice => invoice.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(20);
        // Newest first.
        builder.HasIndex(invoice => invoice.IssuedAt);
    }
}
