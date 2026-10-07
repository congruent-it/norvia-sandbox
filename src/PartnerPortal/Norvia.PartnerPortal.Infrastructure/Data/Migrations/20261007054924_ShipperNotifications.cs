using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
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
                    PersonaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Content_BookingRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content_Contact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content_ExceptionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Content_Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Content_TransportOrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeliverFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliverUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Eta = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipperNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipperNotifications_PersonaId_NotificationId",
                table: "ShipperNotifications",
                columns: new[] { "PersonaId", "NotificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipperNotifications_PersonaId_ReceivedAt",
                table: "ShipperNotifications",
                columns: new[] { "PersonaId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipperNotifications");
        }
    }
}
