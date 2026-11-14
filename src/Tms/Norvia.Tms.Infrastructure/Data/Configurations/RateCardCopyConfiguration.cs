using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Infrastructure.RateCards;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class RateCardCopyConfiguration : IEntityTypeConfiguration<RateCardCopy>
{
    public void Configure(EntityTypeBuilder<RateCardCopy> builder)
    {
        builder.ToTable("RateCardCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.ShipperId).HasMaxLength(50).IsRequired();
        builder.HasIndex(copy => copy.ShipperId);
        builder.Property(copy => copy.VersionsJson).HasColumnType("nvarchar(max)").IsRequired();
    }
}
