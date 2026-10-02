using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierAnswerModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AutoAnswerDueAt",
                table: "CarrierBookingRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoAnswerMode",
                table: "CarrierBookingRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoAnswerReason",
                table: "CarrierBookingRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierAnswerSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DelayMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAnswerSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookingRequests_AutoAnswerDueAt",
                table: "CarrierBookingRequests",
                column: "AutoAnswerDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAnswerSettings_PersonaId",
                table: "CarrierAnswerSettings",
                column: "PersonaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierAnswerSettings");

            migrationBuilder.DropIndex(
                name: "IX_CarrierBookingRequests_AutoAnswerDueAt",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "AutoAnswerDueAt",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "AutoAnswerMode",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "AutoAnswerReason",
                table: "CarrierBookingRequests");
        }
    }
}
