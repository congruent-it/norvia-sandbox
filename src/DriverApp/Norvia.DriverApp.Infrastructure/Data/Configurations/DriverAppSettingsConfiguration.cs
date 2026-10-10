using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.DriverApp.Domain.Settings;

namespace Norvia.DriverApp.Infrastructure.Data.Configurations;

internal sealed class DriverAppSettingsConfiguration : IEntityTypeConfiguration<DriverAppSettings>
{
    public void Configure(EntityTypeBuilder<DriverAppSettings> builder)
    {
        builder.ToTable("DriverAppSettings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
    }
}
