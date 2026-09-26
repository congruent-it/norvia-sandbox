using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransportOrderRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreightCharge",
                table: "TransportOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RatedAt",
                table: "TransportOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingResult",
                table: "TransportOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Every Transport Order accepted before Rating existed came in through the Shipper API, so it waits for
            // Rating now; the rating worker's first sweep rates it. A Rejected booking is never rated.
            migrationBuilder.Sql("UPDATE [TransportOrders] SET [RatingResult] = N'NotYetRated' WHERE [Status] = N'Accepted'");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TransportOrders",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_RatingResult_ReceivedAt",
                table: "TransportOrders",
                columns: new[] { "RatingResult", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_RatingResult_ReceivedAt",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "FreightCharge",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "RatedAt",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "RatingResult",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TransportOrders");
        }
    }
}
