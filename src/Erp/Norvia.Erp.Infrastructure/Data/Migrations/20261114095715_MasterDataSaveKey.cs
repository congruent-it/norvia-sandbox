using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataSaveKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "Vehicles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "Shippers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "RateCards",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "Drivers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "Carriers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSaveKey",
                table: "CarrierRateCards",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "Shippers");

            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "Carriers");

            migrationBuilder.DropColumn(
                name: "LastSaveKey",
                table: "CarrierRateCards");
        }
    }
}
