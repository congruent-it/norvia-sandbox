using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerCorrelationId",
                table: "CarrierBookingRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierBookingReference",
                table: "CarrierBookingRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "CarrierBookingRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedCollection",
                table: "CarrierBookingRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "CarrierBookingRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionText",
                table: "CarrierBookingRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CarrierBookingRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                // Every request already in an inbox is still open: nothing could answer one before this migration.
                defaultValue: "Open");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerCorrelationId",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "CarrierBookingReference",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "PlannedCollection",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "RejectionText",
                table: "CarrierBookingRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CarrierBookingRequests");
        }
    }
}
