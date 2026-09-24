using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShipperReferenceHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipperReference",
                table: "TransportOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            // Transport Orders accepted before references were unique take theirs up. Should a reference already be
            // used twice, the earliest Transport Order holds it.
            migrationBuilder.Sql("""
                WITH Holders AS (
                    SELECT [ShipperReference], JSON_VALUE([Details], '$.shipperReference') AS [Submitted],
                        ROW_NUMBER() OVER (
                            PARTITION BY [ShipperId], JSON_VALUE([Details], '$.shipperReference') COLLATE Latin1_General_100_BIN2
                            ORDER BY [ReceivedAt], [Id]) AS [Rank]
                    FROM [TransportOrders]
                    WHERE [Status] = 'Accepted'
                )
                UPDATE Holders SET [ShipperReference] = [Submitted]
                WHERE [Rank] = 1 AND LEN([Submitted]) BETWEEN 1 AND 100
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_ShipperId_ShipperReference",
                table: "TransportOrders",
                columns: new[] { "ShipperId", "ShipperReference" },
                unique: true,
                filter: "[ShipperReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_ShipperId_ShipperReference",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "ShipperReference",
                table: "TransportOrders");
        }
    }
}
