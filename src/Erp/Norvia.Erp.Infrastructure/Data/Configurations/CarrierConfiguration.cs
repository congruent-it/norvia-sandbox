using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> builder)
    {
        builder.ToTable("Carriers");
        builder.Ignore(carrier => carrier.DomainEvents);
        builder.HasKey(carrier => carrier.Id);
        builder.Property(carrier => carrier.Id).HasConversion(id => id.Value, value => new CarrierId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(carrier => carrier.Name).HasMaxLength(200).IsRequired();
        builder.Property(carrier => carrier.Revision).HasDefaultValue(1);
        builder.Property(carrier => carrier.FinanceOwned).HasDefaultValue(false);
        // Two edits at once conflict instead of both winning; the loser is run again and sees the new revision.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
