using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norvia.PartnerPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierShipmentProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastProgress",
                table: "CarrierBookingRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastProgress",
                table: "CarrierBookingRequests");
        }
    }
}
