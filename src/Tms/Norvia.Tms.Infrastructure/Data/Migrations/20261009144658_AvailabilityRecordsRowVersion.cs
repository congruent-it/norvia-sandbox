using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AvailabilityRecordsRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OffTheRoadDays",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Absences",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OffTheRoadDays");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Absences");
        }
    }
}
