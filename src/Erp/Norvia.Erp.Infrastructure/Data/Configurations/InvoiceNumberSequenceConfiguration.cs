using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class InvoiceNumberSequenceConfiguration : IEntityTypeConfiguration<InvoiceNumberSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSequence> builder)
    {
        builder.ToTable("InvoiceNumberSequences");
        builder.Ignore(sequence => sequence.DomainEvents);
        builder.Ignore(sequence => sequence.Year);
        // The year is the key, so two first runs of a year can't both start its sequence.
        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).HasColumnName("Year").ValueGeneratedNever();
        // Two transactions taking the next number at once conflict, so numbers stay gapless and unique.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
