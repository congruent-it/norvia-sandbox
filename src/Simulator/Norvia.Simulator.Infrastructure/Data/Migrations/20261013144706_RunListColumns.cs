using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Simulator.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunListColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "SimulationRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlannedCount",
                table: "SimulationRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PresetName",
                table: "SimulationRuns",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Seed",
                table: "SimulationRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "PlannedCount",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "PresetName",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "Seed",
                table: "SimulationRuns");
        }
    }
}
