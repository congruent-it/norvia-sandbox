using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes");
        builder.Ignore(note => note.DomainEvents);
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id)
            .HasConversion(number => number.Value, value => new CreditNoteNumber(value))
            .HasMaxLength(20).ValueGeneratedNever();
        builder.Property(note => note.ShipperId).HasConversion(id => id.Value, value => new ShipperId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Shipper>().WithMany().HasForeignKey(note => note.ShipperId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(note => note.InvoiceNumber)
            .HasConversion(number => number.Value, value => new InvoiceNumber(value))
            .HasMaxLength(20).IsRequired();
        // A Credit note can't outlive, or point at, an invoice that doesn't exist.
        builder.HasOne<ShipperInvoice>().WithMany().HasForeignKey(note => note.InvoiceNumber).OnDelete(DeleteBehavior.Restrict);
        builder.Property(note => note.TransportOrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(note => note.ShipperReference).HasMaxLength(200).IsRequired();
        builder.Property(note => note.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(note => note.AmountWithoutVat).HasPrecision(18, 2);
        builder.Property(note => note.Vat).HasPrecision(18, 2);
        builder.Property(note => note.TotalWithVat).HasPrecision(18, 2);
        builder.Property(note => note.Reason).HasMaxLength(CreditNote.MaxReasonLength).IsRequired();
        builder.Property(note => note.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(note => note.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(note => note.NotDeliveredMessage).HasMaxLength(ShipperInvoice.MaxCauseLength);
        // The Credit notes of an invoice line, summed against its total, and newest first.
        builder.HasIndex(note => new { note.InvoiceNumber, note.TransportOrderNumber });
        builder.HasIndex(note => note.IssuedAt);
    }
}
