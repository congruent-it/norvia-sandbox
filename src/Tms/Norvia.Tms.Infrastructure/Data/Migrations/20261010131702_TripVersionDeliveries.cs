using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripVersionDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripVersionDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Cause = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripVersionDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripVersionDeliveries_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripVersionDeliveries_TripId_Version",
                table: "TripVersionDeliveries",
                columns: new[] { "TripId", "Version" },
                unique: true);

            // A Trip dispatched before delivery states existed has sent its latest version, and nothing has reported on it.
            migrationBuilder.Sql(
                "INSERT INTO [TripVersionDeliveries] ([TripId], [Version], [State]) SELECT [Id], [Version], 'Sending' FROM [Trips] WHERE [Version] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripVersionDeliveries");
        }
    }
}
