using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DepartureLat",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DepartureLng",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestinationLat",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestinationLng",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLat",
                table: "RideRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLng",
                table: "RideRequests",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepartureLat",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "DepartureLng",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "DestinationLat",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "DestinationLng",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "PickupLat",
                table: "RideRequests");

            migrationBuilder.DropColumn(
                name: "PickupLng",
                table: "RideRequests");
        }
    }
}
