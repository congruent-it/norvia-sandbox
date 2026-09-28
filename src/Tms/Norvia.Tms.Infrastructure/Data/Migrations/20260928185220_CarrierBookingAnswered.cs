using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingAnswered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerCorrelationId",
                table: "CarrierBookings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnsweredAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierBookingReference",
                table: "CarrierBookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedCollectionTime",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "CarrierBookings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasonText",
                table: "CarrierBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerCorrelationId",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CarrierBookingReference",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "PlannedCollectionTime",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "RejectionReasonText",
                table: "CarrierBookings");
        }
    }
}
