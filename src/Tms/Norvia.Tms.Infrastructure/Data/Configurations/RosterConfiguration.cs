using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

/// <summary>A Roster the Planner changed: one per Driver, with the weekdays as one JSON column.</summary>
internal sealed class RosterConfiguration : IEntityTypeConfiguration<Roster>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Roster> builder)
    {
        builder.ToTable("Rosters");
        builder.Ignore("EqualityContract");

        builder.HasKey(roster => roster.DriverId);
        builder.Property(roster => roster.DriverId).HasConversion(id => id.Value, value => new DriverId(value)).HasMaxLength(20).ValueGeneratedNever();

        builder.Property(roster => roster.Weekdays)
            .HasConversion(
                weekdays => JsonSerializer.Serialize(
                    weekdays.ToDictionary(entry => entry.Key.ToString(), entry => new StoredShift(entry.Value.Start, entry.Value.End)), Json),
                json => (IReadOnlyDictionary<DayOfWeek, ShiftTimes>)JsonSerializer.Deserialize<Dictionary<string, StoredShift>>(json, Json)!
                    .ToDictionary(entry => Enum.Parse<DayOfWeek>(entry.Key), entry => new ShiftTimes(entry.Value.Start, entry.Value.End)),
                new ValueComparer<IReadOnlyDictionary<DayOfWeek, ShiftTimes>>(
                    (left, right) => left!.Count == right!.Count && left.All(entry => right.ContainsKey(entry.Key) && right[entry.Key] == entry.Value),
                    weekdays => weekdays.Count,
                    weekdays => weekdays.ToDictionary()))
            .IsRequired();
    }

    private sealed record StoredShift(TimeOnly Start, TimeOnly End);
}
