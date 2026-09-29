using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBookingReportAndWithdraw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotDeliveredAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotDeliveredMessage",
                table: "CarrierBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotDeliveredStatusCode",
                table: "CarrierBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalCorrelationId",
                table: "CarrierBookings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalKey",
                table: "CarrierBookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WithdrawnAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotDeliveredAt",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "NotDeliveredMessage",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "NotDeliveredStatusCode",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "WithdrawalCorrelationId",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "WithdrawalKey",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "WithdrawnAt",
                table: "CarrierBookings");
        }
    }
}
