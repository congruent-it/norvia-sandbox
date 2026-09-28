using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Infrastructure.Outbox;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => message.EventId).IsUnique();
        builder.HasIndex(message => message.Id).HasFilter("[PublishedAt] IS NULL").HasDatabaseName("IX_OutboxMessages_Unpublished");
        builder.Property(message => message.Topic).HasMaxLength(260);
        builder.Property(message => message.EventType).HasMaxLength(100);
        builder.Property(message => message.CorrelationId).HasMaxLength(128);
        // A W3C traceparent: version, trace ID, span ID and flags, 55 characters.
        builder.Property(message => message.TraceParent).HasMaxLength(55);
    }
}
