using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.Tms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransportOrderRejections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_Number",
                table: "TransportOrders");

            migrationBuilder.AlterColumn<string>(
                name: "Number",
                table: "TransportOrders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasons",
                table: "TransportOrders",
                type: "nvarchar(max)",
                nullable: false,
                // Transport Orders accepted before rejections existed have no reasons, not an empty string.
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_Number",
                table: "TransportOrders",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Before rejections, only numbered Transport Orders existed; Rejected bookings can't survive the rollback.
            migrationBuilder.Sql("DELETE FROM [TransportOrders] WHERE [Number] IS NULL");

            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_Number",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "RejectionReasons",
                table: "TransportOrders");

            migrationBuilder.AlterColumn<string>(
                name: "Number",
                table: "TransportOrders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_Number",
                table: "TransportOrders",
                column: "Number",
                unique: true);
        }
    }
}
