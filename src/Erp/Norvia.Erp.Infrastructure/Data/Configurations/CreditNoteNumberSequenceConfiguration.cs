using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CreditNoteNumberSequenceConfiguration : IEntityTypeConfiguration<CreditNoteNumberSequence>
{
    public void Configure(EntityTypeBuilder<CreditNoteNumberSequence> builder)
    {
        builder.ToTable("CreditNoteNumberSequences");
        builder.Ignore(sequence => sequence.DomainEvents);
        builder.Ignore(sequence => sequence.Year);
        // The year is the key, so two first Credit notes of a year can't both start its sequence.
        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).HasColumnName("Year").ValueGeneratedNever();
        // Two transactions taking the next number at once conflict, so numbers stay gapless and unique.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
