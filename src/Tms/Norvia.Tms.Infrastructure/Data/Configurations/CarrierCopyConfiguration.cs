using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class CarrierCopyConfiguration : IEntityTypeConfiguration<CarrierCopy>
{
    public void Configure(EntityTypeBuilder<CarrierCopy> builder)
    {
        builder.ToTable("CarrierCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.Name).HasMaxLength(200).IsRequired();
    }
}
