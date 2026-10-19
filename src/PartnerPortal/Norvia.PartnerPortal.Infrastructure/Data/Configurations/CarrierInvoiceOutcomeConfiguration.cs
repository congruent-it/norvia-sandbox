using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class CarrierInvoiceOutcomeConfiguration : IEntityTypeConfiguration<CarrierInvoiceOutcome>
{
    private static readonly JsonSerializerOptions LinesJson = JsonOptions.WithEnumNames;

    public void Configure(EntityTypeBuilder<CarrierInvoiceOutcome> builder)
    {
        builder.ToTable("CarrierInvoiceOutcomes");
        builder.Ignore(outcome => outcome.DomainEvents);

        builder.HasKey(outcome => outcome.Id);
        builder.Property(outcome => outcome.Id)
            .HasConversion(id => id.Value, value => new CarrierInvoiceOutcomeId(value))
            .ValueGeneratedNever();

        builder.Property(outcome => outcome.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(outcome => outcome.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(outcome => outcome.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(outcome => outcome.ApprovedAmount).HasPrecision(18, 2);

        // The invoice number is the key: an outcome is stored once per invoice number, whatever arrives twice.
        builder.HasIndex(outcome => new { outcome.PersonaId, outcome.InvoiceNumber }).IsUnique();

        builder.Property(outcome => outcome.Lines)
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, LinesJson),
                json => JsonSerializer.Deserialize<List<CarrierInvoiceOutcomeLine>>(json, LinesJson)!,
                new ValueComparer<IReadOnlyList<CarrierInvoiceOutcomeLine>>(
                    (left, right) => JsonSerializer.Serialize(left, LinesJson) == JsonSerializer.Serialize(right, LinesJson),
                    lines => JsonSerializer.Serialize(lines, LinesJson).GetHashCode(StringComparison.Ordinal),
                    lines => JsonSerializer.Deserialize<List<CarrierInvoiceOutcomeLine>>(JsonSerializer.Serialize(lines, LinesJson), LinesJson)!))
            .IsRequired();
    }
}
