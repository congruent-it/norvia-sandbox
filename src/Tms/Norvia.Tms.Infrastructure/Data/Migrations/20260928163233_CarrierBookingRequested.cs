using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingRequested : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "CarrierBookingNumbers");

            migrationBuilder.AddColumn<string>(
                name: "CarrierBookingNumber",
                table: "Shipments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CarrierId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierBookings_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookings_Number",
                table: "CarrierBookings",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookings_ShipmentId",
                table: "CarrierBookings",
                column: "ShipmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CarrierBookingNumber",
                table: "Shipments");

            migrationBuilder.DropSequence(
                name: "CarrierBookingNumbers");
        }
    }
}
