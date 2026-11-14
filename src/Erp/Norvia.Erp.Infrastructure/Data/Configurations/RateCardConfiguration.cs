using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("RateCards");
        builder.Ignore(card => card.DomainEvents);
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).HasConversion(id => id.Value, value => new RateCardId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(card => card.ShipperId).HasConversion(id => id.Value, value => new ShipperId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Shipper>().WithMany().HasForeignKey(card => card.ShipperId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(card => card.Versions).AsJsonDocument();
        builder.Property(card => card.Revision).HasDefaultValue(1);
        builder.Property(card => card.FinanceOwned).HasDefaultValue(false);
        builder.Property(card => card.LastSaveKey).HasMaxLength(MasterDataSaveKey.MaxLength);
        // Two edits at once conflict instead of both winning; the loser is run again and sees the new revision.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
