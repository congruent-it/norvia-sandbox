using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Infrastructure.RateCards;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class CarrierRateCardCopyConfiguration : IEntityTypeConfiguration<CarrierRateCardCopy>
{
    public void Configure(EntityTypeBuilder<CarrierRateCardCopy> builder)
    {
        builder.ToTable("CarrierRateCardCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.CarrierId).HasMaxLength(50).IsRequired();
        builder.HasIndex(copy => copy.CarrierId);
        builder.Property(copy => copy.VersionsJson).HasColumnType("nvarchar(max)").IsRequired();
    }
}
