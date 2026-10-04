using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackingEventRecorded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CarrierBookingReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Eta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MovedShipment = table.Column<bool>(type: "bit", nullable: false),
                    LocationCountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    LocationPlace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LocationPostcode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvents_ShipmentId",
                table: "TrackingEvents",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvents_SourceId_SourceEventId",
                table: "TrackingEvents",
                columns: new[] { "SourceId", "SourceEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackingEvents");
        }
    }
}
