using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierInvoiceOutcomeDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotToldAt",
                table: "CarrierInvoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotToldMessage",
                table: "CarrierInvoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotToldStatusCode",
                table: "CarrierInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToldAt",
                table: "CarrierInvoices",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotToldAt",
                table: "CarrierInvoices");

            migrationBuilder.DropColumn(
                name: "NotToldMessage",
                table: "CarrierInvoices");

            migrationBuilder.DropColumn(
                name: "NotToldStatusCode",
                table: "CarrierInvoices");

            migrationBuilder.DropColumn(
                name: "ToldAt",
                table: "CarrierInvoices");
        }
    }
}
