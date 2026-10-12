using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackingEventOwnFleetTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriverId",
                table: "TrackingEvents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TripNumber",
                table: "TrackingEvents",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TripVersion",
                table: "TrackingEvents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvents_TripNumber",
                table: "TrackingEvents",
                column: "TripNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackingEvents_TripNumber",
                table: "TrackingEvents");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "TrackingEvents");

            migrationBuilder.DropColumn(
                name: "TripNumber",
                table: "TrackingEvents");

            migrationBuilder.DropColumn(
                name: "TripVersion",
                table: "TrackingEvents");
        }
    }
}
