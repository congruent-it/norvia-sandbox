using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierTrackingModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AutoProgressDelayMilliseconds",
                table: "CarrierBookingRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AutoProgressDueAt",
                table: "CarrierBookingRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierTrackingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DelayMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierTrackingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookingRequests_AutoProgressDueAt",
                table: "CarrierBookingRequests",
                column: "AutoProgressDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingSettings_PersonaId",
                table: "CarrierTrackingSettings",
                column: "PersonaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierTrackingSettings");

            migrationBuilder.DropIndex(
                name: "IX_CarrierBookingRequests_AutoProgressDueAt",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "AutoProgressDelayMilliseconds",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "AutoProgressDueAt",
                table: "CarrierBookingRequests");
        }
    }
}
