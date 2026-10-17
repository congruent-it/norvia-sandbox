using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransportOrderNumberTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NumberText",
                table: "TransportOrders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "[Number]");

            migrationBuilder.AddColumn<string>(
                name: "ShipmentNumberText",
                table: "TransportOrders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "[ShipmentNumber]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumberText",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "ShipmentNumberText",
                table: "TransportOrders");
        }
    }
}
