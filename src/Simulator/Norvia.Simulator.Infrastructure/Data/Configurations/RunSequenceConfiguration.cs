using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Infrastructure.Data.Configurations;

internal sealed class RunSequenceConfiguration : IEntityTypeConfiguration<RunSequence>
{
    public void Configure(EntityTypeBuilder<RunSequence> builder)
    {
        builder.ToTable("RunSequence");
        builder.Ignore(sequence => sequence.DomainEvents);
        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).ValueGeneratedNever();
    }
}
