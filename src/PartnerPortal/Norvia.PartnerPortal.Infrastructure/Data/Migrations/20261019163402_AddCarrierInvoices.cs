using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierInvoiceOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Lines = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierInvoiceOutcomes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Lines = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierInvoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierInvoiceOutcomes_PersonaId_InvoiceNumber",
                table: "CarrierInvoiceOutcomes",
                columns: new[] { "PersonaId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierInvoices_PersonaId_InvoiceNumber",
                table: "CarrierInvoices",
                columns: new[] { "PersonaId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierInvoices_PersonaId_SubmittedAt",
                table: "CarrierInvoices",
                columns: new[] { "PersonaId", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierInvoiceOutcomes");

            migrationBuilder.DropTable(
                name: "CarrierInvoices");
        }
    }
}
