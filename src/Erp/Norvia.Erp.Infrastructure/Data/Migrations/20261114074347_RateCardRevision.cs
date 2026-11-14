using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RateCardRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FinanceOwned",
                table: "RateCards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "RateCards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RateCards",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FinanceOwned",
                table: "CarrierRateCards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CarrierRateCards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CarrierRateCards",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinanceOwned",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "FinanceOwned",
                table: "CarrierRateCards");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CarrierRateCards");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CarrierRateCards");
        }
    }
}
