using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Infrastructure.Data.Configurations;

internal sealed class DriverReportConfiguration : IEntityTypeConfiguration<DriverReport>
{
    public void Configure(EntityTypeBuilder<DriverReport> builder)
    {
        builder.ToTable("DriverReports");
        builder.Ignore(report => report.IsActiveStart);
        builder.Ignore(report => report.GivesStopAnOutcome);

        // The key is the Driver app event ID, which names the report to the Logic App and the TMS.
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).HasColumnName("EventId").ValueGeneratedNever()
            .HasConversion(id => id.Value, value => new ReportId(value));

        builder.Property(report => report.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(report => report.TripNumber).HasMaxLength(16).HasConversion(number => number.Value, value => new TripNumber(value));
        builder.Property(report => report.ShipmentNumber).HasMaxLength(16);
        builder.Property(report => report.Stop).HasConversion<string>().HasMaxLength(16);
        builder.Property(report => report.Remark).HasMaxLength(500);
        builder.Property(report => report.DriverId).HasMaxLength(32).HasConversion(id => id.Value, value => new DriverId(value));
        builder.Property(report => report.CorrelationId).HasMaxLength(128);
        builder.Property(report => report.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(report => report.FailureCode).HasMaxLength(100);
        builder.Property(report => report.Message).HasMaxLength(1000);

        // The Trip screen reads a Trip's reports, and Start Trip looks for one that is still active.
        builder.HasIndex(report => new { report.TripNumber, report.EventTime });
    }
}
