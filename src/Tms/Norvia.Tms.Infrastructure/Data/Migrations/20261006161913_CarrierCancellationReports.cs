using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierCancellationReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CarrierNotToldAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierNotToldMessage",
                table: "CarrierBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarrierNotToldStatusCode",
                table: "CarrierBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CarrierToldAt",
                table: "CarrierBookings",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarrierNotToldAt",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CarrierNotToldMessage",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CarrierNotToldStatusCode",
                table: "CarrierBookings");

            migrationBuilder.DropColumn(
                name: "CarrierToldAt",
                table: "CarrierBookings");
        }
    }
}
