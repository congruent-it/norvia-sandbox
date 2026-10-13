using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Infrastructure.Data.Configurations;

internal sealed class SimulationRunConfiguration : IEntityTypeConfiguration<SimulationRun>
{
    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<SimulationRun> builder)
    {
        builder.ToTable("SimulationRuns");
        builder.Ignore(run => run.DomainEvents);
        builder.Ignore(run => run.Settings);
        builder.Ignore(run => run.IsActive);

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new SimulationRunId(value))
            .ValueGeneratedNever();

        builder.HasIndex(run => run.Number).IsUnique();
        builder.HasIndex(run => run.StartedAt);

        // Cancel, complete and abandon can all change a run, so a stale write fails instead of undoing another's.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        // One active run at a time, whatever starts them: a second start fails here even if both passed the check.
        builder.HasIndex(run => run.Status).IsUnique().HasFilter("[Status] = 'Running'").HasDatabaseName("IX_SimulationRuns_OneActive");
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.PresetName).HasMaxLength(100);
        builder.Property(run => run.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.StartedBy).HasMaxLength(200).IsRequired();

        // The plan is kept whole, as one JSON document: it's only ever read back as a whole (the preview matches what is sent).
        builder.Property(run => run.Plan)
            .HasConversion(
                plan => JsonSerializer.Serialize(plan, PlanJson),
                json => JsonSerializer.Deserialize<SimulationPlan>(json, PlanJson)!,
                new ValueComparer<SimulationPlan>(
                    (left, right) => ReferenceEquals(left, right),
                    plan => plan.GetHashCode(),
                    plan => plan))
            .IsRequired();
    }
}
