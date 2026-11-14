using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.Drivers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");
        builder.Ignore(driver => driver.DomainEvents);
        builder.HasKey(driver => driver.Id);
        builder.Property(driver => driver.Id).HasConversion(id => id.Value, value => new DriverId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(driver => driver.Name).HasMaxLength(200).IsRequired();

        // The licence categories are kept as one comma-separated text, such as "B,C,CE".
        builder.Property(driver => driver.Licences)
            .HasConversion(
                licences => string.Join(',', licences),
                text => text.Length == 0
                    ? new List<LicenceCategory>()
                    : text.Split(',', StringSplitOptions.None).Select(Enum.Parse<LicenceCategory>).ToList(),
                new ValueComparer<IReadOnlyList<LicenceCategory>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, licence) => HashCode.Combine(hash, licence)),
                    list => list.ToList()))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(driver => driver.Revision).HasDefaultValue(1);
        builder.Property(driver => driver.FinanceOwned).HasDefaultValue(false);
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
