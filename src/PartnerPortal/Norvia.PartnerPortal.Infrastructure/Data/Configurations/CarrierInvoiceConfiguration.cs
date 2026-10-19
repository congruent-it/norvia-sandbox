using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class CarrierInvoiceConfiguration : IEntityTypeConfiguration<CarrierInvoice>
{
    private static readonly JsonSerializerOptions LinesJson = JsonOptions.WithEnumNames;

    public void Configure(EntityTypeBuilder<CarrierInvoice> builder)
    {
        builder.ToTable("CarrierInvoices");
        builder.Ignore(invoice => invoice.DomainEvents);
        builder.Ignore(invoice => invoice.Total);

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id)
            .HasConversion(id => id.Value, value => new CarrierInvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(invoice => invoice.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(invoice => invoice.ApprovedAmount).HasPrecision(18, 2);

        // The invoice number is the key of a Persona's list, so an invoice that is sent twice is stored once.
        builder.HasIndex(invoice => new { invoice.PersonaId, invoice.InvoiceNumber }).IsUnique();
        builder.HasIndex(invoice => new { invoice.PersonaId, invoice.SubmittedAt });

        // The lines are kept whole, as one JSON document: they're only ever read back with the invoice.
        builder.Property(invoice => invoice.Lines)
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, LinesJson),
                json => JsonSerializer.Deserialize<List<CarrierInvoiceLine>>(json, LinesJson)!,
                new ValueComparer<IReadOnlyList<CarrierInvoiceLine>>(
                    (left, right) => JsonSerializer.Serialize(left, LinesJson) == JsonSerializer.Serialize(right, LinesJson),
                    lines => JsonSerializer.Serialize(lines, LinesJson).GetHashCode(StringComparison.Ordinal),
                    lines => JsonSerializer.Deserialize<List<CarrierInvoiceLine>>(JsonSerializer.Serialize(lines, LinesJson), LinesJson)!))
            .IsRequired();
    }
}
