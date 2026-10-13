using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Simulator.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SimulationRuns",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_OneActive",
                table: "SimulationRuns",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SimulationRuns_OneActive",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SimulationRuns");
        }
    }
}
