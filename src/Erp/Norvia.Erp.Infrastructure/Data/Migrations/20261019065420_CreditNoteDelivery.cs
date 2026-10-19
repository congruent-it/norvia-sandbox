using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Erp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreditNoteDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotDeliveredAt",
                table: "CreditNotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotDeliveredMessage",
                table: "CreditNotes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotDeliveredStatusCode",
                table: "CreditNotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "CreditNotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CreditNotes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Issued");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotDeliveredAt",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "NotDeliveredMessage",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "NotDeliveredStatusCode",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CreditNotes");
        }
    }
}
