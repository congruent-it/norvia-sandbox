using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransportOrderBillable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BecameBillableAt",
                table: "TransportOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCorrelationId",
                table: "TransportOrders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "TransportOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "TransportOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReleasedWithoutProofOfDelivery",
                table: "TransportOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Accepted Transport Orders from before billing: a Fulfilment order is billed through the Fulfilment Contract, and
            // every other one has not been made Billable yet. A Rejected booking has no billing state.
            migrationBuilder.Sql(
                "UPDATE TransportOrders SET BillingState = CASE WHEN Channel = 'Wms' THEN 'NotBilledSeparately' ELSE 'NotYetBillable' END "
                + "WHERE Status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_BillingState_ReceivedAt",
                table: "TransportOrders",
                columns: new[] { "BillingState", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_BillingState_ReceivedAt",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "BecameBillableAt",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "BillingCorrelationId",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "ReleasedWithoutProofOfDelivery",
                table: "TransportOrders");
        }
    }
}
