using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class ExceptionCaseConfiguration : IEntityTypeConfiguration<ExceptionCase>
{
    private static readonly JsonSerializerOptions JsonColumns = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ExceptionCase> builder)
    {
        builder.ToTable("Exceptions");
        builder.Ignore(exception => exception.DomainEvents);
        builder.Ignore(exception => exception.CarriedBy);

        builder.HasKey(exception => exception.Id);
        builder.Property(exception => exception.Id)
            .HasConversion(id => id.Value, value => new ExceptionId(value))
            .ValueGeneratedNever();

        builder.Property(exception => exception.Number)
            .HasConversion(number => number.Value, value => ExceptionNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();
        builder.HasIndex(exception => exception.Number).IsUnique();

        // A Shipment's Exceptions: many over its life, never deleted with it.
        builder.Property(exception => exception.ShipmentId)
            .HasConversion(id => id.Value, value => new ShipmentId(value));
        builder.HasOne<Shipment>().WithMany().HasForeignKey(exception => exception.ShipmentId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(exception => exception.ShipmentNumber)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(exception => exception.TransportOrderNumber)
            .HasConversion(number => number.Value, value => TransportOrderNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(exception => exception.ShipperId)
            .HasConversion(id => id.Value, value => new ShipperId(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(exception => exception.CarrierId)
            .HasConversion(id => id!.Value, value => new CarrierId(value))
            .HasMaxLength(50);

        builder.Property(exception => exception.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(exception => exception.Status).HasConversion<string>().HasMaxLength(20);

        // A Shipment has at most one open Exception of each type, even when two causes arrive at the same moment.
        builder.HasIndex(exception => new { exception.ShipmentId, exception.Type })
            .IsUnique()
            .HasFilter($"[Status] = '{nameof(ExceptionStatus.Open)}'");

        // The Planner's list of Open Exceptions, oldest first.
        builder.HasIndex(exception => new { exception.Status, exception.OpenedAt });

        builder.Property(exception => exception.AfterDelivered);
        builder.Property(exception => exception.CauseCleared);

        builder.Property(exception => exception.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(exception => exception.OpenedAt).IsRequired();

        // The causes are read and written only with their Exception, so they are one JSON column. An Exception always has the
        // cause that opened it, so the column is never empty.
        builder.Property(exception => exception.Causes)
            .HasConversion(
                causes => JsonSerializer.Serialize(causes, JsonColumns),
                json => JsonSerializer.Deserialize<ExceptionCause[]>(json, JsonColumns)!,
                new ValueComparer<IReadOnlyList<ExceptionCause>>(
                    (left, right) => left!.SequenceEqual(right!),
                    causes => causes.Count,
                    causes => causes.ToArray()))
            .IsRequired();

        // The notes are read and written only with their Exception too.
        builder.Property(exception => exception.Notes)
            .HasConversion(
                notes => JsonSerializer.Serialize(notes, JsonColumns),
                json => JsonSerializer.Deserialize<ExceptionNote[]>(json, JsonColumns)!,
                new ValueComparer<IReadOnlyList<ExceptionNote>>(
                    (left, right) => left!.SequenceEqual(right!),
                    notes => notes.Count,
                    notes => notes.ToArray()))
            .HasDefaultValueSql("N'[]'")
            .IsRequired();

        builder.Property(exception => exception.Outcome).HasConversion<string>().HasMaxLength(30);
        builder.Property(exception => exception.Remark).HasMaxLength(2000);
        builder.Property(exception => exception.ResolvedAt);

        builder.Property(exception => exception.ResolutionCorrelationId)
            .HasConversion(id => id!.Value, value => new CorrelationId(value))
            .HasMaxLength(128);

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
