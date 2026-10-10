using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.DriverApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverAppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Unavailable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverAppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverTrips",
                columns: table => new
                {
                    TripNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverTrips", x => x.TripNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_DriverId",
                table: "DriverTrips",
                column: "DriverId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverAppSettings");

            migrationBuilder.DropTable(
                name: "DriverTrips");
        }
    }
}
