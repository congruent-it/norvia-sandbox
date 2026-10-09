using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AbsencesOffTheRoadDaysAndRosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Absences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Absences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffTheRoadDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffTheRoadDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rosters",
                columns: table => new
                {
                    DriverId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Weekdays = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rosters", x => x.DriverId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_Date",
                table: "Absences",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Absences_DriverId_Date",
                table: "Absences",
                columns: new[] { "DriverId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffTheRoadDays_Date",
                table: "OffTheRoadDays",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_OffTheRoadDays_VehicleId_Date",
                table: "OffTheRoadDays",
                columns: new[] { "VehicleId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Absences");

            migrationBuilder.DropTable(
                name: "OffTheRoadDays");

            migrationBuilder.DropTable(
                name: "Rosters");
        }
    }
}
