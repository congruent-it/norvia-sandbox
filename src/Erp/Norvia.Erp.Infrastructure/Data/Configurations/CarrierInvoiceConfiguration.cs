using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CarrierInvoiceConfiguration : IEntityTypeConfiguration<CarrierInvoice>
{
    public void Configure(EntityTypeBuilder<CarrierInvoice> builder)
    {
        builder.ToTable("CarrierInvoices");
        builder.Ignore(invoice => invoice.DomainEvents);
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).HasConversion(id => id.Value, value => new CarrierInvoiceId(value)).ValueGeneratedNever();
        builder.Property(invoice => invoice.CarrierId).HasConversion(id => id.Value, value => new CarrierId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Carrier>().WithMany().HasForeignKey(invoice => invoice.CarrierId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(CarrierInvoice.MaxInvoiceNumberLength).IsRequired();
        builder.Property(invoice => invoice.Lines).AsLinesDocument();
        builder.Property(invoice => invoice.Total).HasPrecision(18, 2);
        builder.Property(invoice => invoice.ApprovedAmount).HasPrecision(18, 2);
        builder.Property(invoice => invoice.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(20);
        // A Carrier's invoice number is unique, so two sends of one Carrier invoice can't both be stored: the loser is run again and finds the first.
        builder.HasIndex(invoice => new { invoice.CarrierId, invoice.InvoiceNumber }).IsUnique();
        // Newest first.
        builder.HasIndex(invoice => invoice.ReceivedAt);
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
