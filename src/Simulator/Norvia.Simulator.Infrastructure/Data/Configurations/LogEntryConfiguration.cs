using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Infrastructure.Data.Configurations;

internal sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable("LogEntries");
        builder.Ignore(entry => entry.DomainEvents);

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id)
            .HasConversion(id => id.Value, value => new LogEntryId(value))
            .ValueGeneratedNever();

        builder.Property(entry => entry.RunId)
            .HasConversion(id => id.Value, value => new SimulationRunId(value))
            .IsRequired();
        builder.HasIndex(entry => new { entry.RunId, entry.Sequence });

        builder.Property(entry => entry.Actor).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(300).IsRequired();
        builder.Property(entry => entry.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(100);
        builder.Property(entry => entry.ShipperReference).HasMaxLength(100);
        builder.Property(entry => entry.TransportOrderNumber).HasMaxLength(50);
        builder.Property(entry => entry.Detail).HasMaxLength(500);
    }
}
