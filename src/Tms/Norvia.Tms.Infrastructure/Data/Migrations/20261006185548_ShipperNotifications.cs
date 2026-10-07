using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShipperNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipperNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TransportOrderNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShipperId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipperReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Eta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AgreedDeliveryEarliest = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AgreedDeliveryLatest = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Planner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotDeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotDeliveredStatusCode = table.Column<int>(type: "int", nullable: true),
                    NotDeliveredMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipperNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipperNotifications_Exceptions_ExceptionId",
                        column: x => x.ExceptionId,
                        principalTable: "Exceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipperNotifications_ExceptionId_RequestedAt",
                table: "ShipperNotifications",
                columns: new[] { "ExceptionId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipperNotifications");
        }
    }
}
