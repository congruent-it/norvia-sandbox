using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Infrastructure.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class DriverCopyConfiguration : IEntityTypeConfiguration<DriverCopy>
{
    public void Configure(EntityTypeBuilder<DriverCopy> builder)
    {
        builder.ToTable("DriverCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.Name).HasMaxLength(200).IsRequired();
        builder.Property(copy => copy.Licences).HasMaxLength(20).IsRequired();
    }
}
