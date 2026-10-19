using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShipperInvoiceNotDelivered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotDeliveredAt",
                table: "ShipperInvoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotDeliveredMessage",
                table: "ShipperInvoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotDeliveredStatusCode",
                table: "ShipperInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ShipperInvoices",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotDeliveredAt",
                table: "ShipperInvoices");

            migrationBuilder.DropColumn(
                name: "NotDeliveredMessage",
                table: "ShipperInvoices");

            migrationBuilder.DropColumn(
                name: "NotDeliveredStatusCode",
                table: "ShipperInvoices");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ShipperInvoices");
        }
    }
}
