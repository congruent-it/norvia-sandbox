using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierBookingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CarrierBookingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierBookingRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookingRequests_PersonaId_CarrierBookingNumber",
                table: "CarrierBookingRequests",
                columns: new[] { "PersonaId", "CarrierBookingNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookingRequests_PersonaId_ReceivedAt",
                table: "CarrierBookingRequests",
                columns: new[] { "PersonaId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierBookingRequests");
        }
    }
}
