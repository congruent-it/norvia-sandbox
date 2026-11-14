using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class CarrierRateCardConfiguration : IEntityTypeConfiguration<CarrierRateCard>
{
    public void Configure(EntityTypeBuilder<CarrierRateCard> builder)
    {
        builder.ToTable("CarrierRateCards");
        builder.Ignore(card => card.DomainEvents);
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).HasConversion(id => id.Value, value => new RateCardId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(card => card.CarrierId).HasConversion(id => id.Value, value => new CarrierId(value)).HasMaxLength(50).IsRequired();
        builder.HasOne<Carrier>().WithMany().HasForeignKey(card => card.CarrierId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(card => card.Versions).AsJsonDocument();
        builder.Property(card => card.Revision).HasDefaultValue(1);
        builder.Property(card => card.FinanceOwned).HasDefaultValue(false);
        builder.Property(card => card.LastSaveKey).HasMaxLength(MasterDataSaveKey.MaxLength);
        // Two edits at once conflict instead of both winning; the loser is run again and sees the new revision.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
