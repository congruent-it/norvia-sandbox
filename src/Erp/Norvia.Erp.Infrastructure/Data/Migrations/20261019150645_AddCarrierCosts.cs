using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierCosts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CarrierBookingReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CarrierId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransportOrderNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Charge = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeliveryTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierCosts_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCosts_CarrierId_CarrierBookingReference",
                table: "CarrierCosts",
                columns: new[] { "CarrierId", "CarrierBookingReference" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCosts_CarrierId_Status",
                table: "CarrierCosts",
                columns: new[] { "CarrierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierCosts");
        }
    }
}
