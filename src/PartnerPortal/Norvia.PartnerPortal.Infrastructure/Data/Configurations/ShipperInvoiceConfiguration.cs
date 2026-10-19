using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class ShipperInvoiceConfiguration : IEntityTypeConfiguration<ShipperInvoice>
{
    private static readonly JsonSerializerOptions ContentJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<ShipperInvoice> builder)
    {
        builder.ToTable("ShipperInvoices");
        builder.Ignore(invoice => invoice.DomainEvents);

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id)
            .HasConversion(id => id.Value, value => new ShipperInvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(invoice => invoice.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(invoice => invoice.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.CorrelationId).HasMaxLength(200).IsRequired();

        // The document number is the key of a Persona's inbox, so a document that arrives twice is stored once.
        builder.HasIndex(invoice => new { invoice.PersonaId, invoice.DocumentNumber }).IsUnique();
        builder.HasIndex(invoice => new { invoice.PersonaId, invoice.ReceivedAt });

        // The document is kept whole, as one JSON document: it's only ever read back as a whole, with its lines.
        builder.Property(invoice => invoice.Content)
            .HasConversion(
                content => JsonSerializer.Serialize(content, ContentJson),
                json => JsonSerializer.Deserialize<ShipperInvoiceContent>(json, ContentJson)!,
                new ValueComparer<ShipperInvoiceContent>(
                    (left, right) => JsonSerializer.Serialize(left, ContentJson) == JsonSerializer.Serialize(right, ContentJson),
                    content => JsonSerializer.Serialize(content, ContentJson).GetHashCode(StringComparison.Ordinal),
                    content => JsonSerializer.Deserialize<ShipperInvoiceContent>(JsonSerializer.Serialize(content, ContentJson), ContentJson)!))
            .IsRequired();
    }
}
