using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RateCardCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierRateCardCopies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CarrierId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    VersionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierRateCardCopies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RateCardCopies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipperId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    VersionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCardCopies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRateCardCopies_CarrierId",
                table: "CarrierRateCardCopies",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardCopies_ShipperId",
                table: "RateCardCopies",
                column: "ShipperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierRateCardCopies");

            migrationBuilder.DropTable(
                name: "RateCardCopies");
        }
    }
}
