using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.DriverApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDriverAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverAppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
