using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentAssignmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Assignments",
                table: "Shipments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // A Shipment planned before the history existed starts it with its planning's assignment.
            migrationBuilder.Sql(
                """
                UPDATE Shipments
                SET Assignments = (
                    SELECT CarrierId AS carrierId, CorrelationId AS correlationId, PlannedAt AS assignedAt, NULL AS [key]
                    FOR JSON PATH, INCLUDE_NULL_VALUES)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Assignments",
                table: "Shipments");
        }
    }
}
