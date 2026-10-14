using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Simulator.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LogShipmentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipmentNumber",
                table: "LogEntries",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipmentNumber",
                table: "LogEntries");
        }
    }
}
