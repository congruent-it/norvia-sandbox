using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationCorrelationId",
                table: "CarrierBookings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationExceptionNumber",
                table: "CarrierBookings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationKey",
                table: "CarrierBookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReasonText",
                table: "CarrierBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationCorrelationId",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CancellationExceptionNumber",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CancellationKey",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CancellationReasonText",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "CarrierBookings");
        }
    }
}
