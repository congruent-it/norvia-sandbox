using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.DriverApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriverReportStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "DriverReports",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipmentNumber",
                table: "DriverReports",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stop",
                table: "DriverReports",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Remark",
                table: "DriverReports");

            migrationBuilder.DropColumn(
                name: "ShipmentNumber",
                table: "DriverReports");

            migrationBuilder.DropColumn(
                name: "Stop",
                table: "DriverReports");
        }
    }
}
