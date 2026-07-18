using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideLiveTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LastLat",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LastLng",
                table: "Rides",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLocationAt",
                table: "Rides",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TripStartedAt",
                table: "Rides",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLat",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "LastLng",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "LastLocationAt",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "TripStartedAt",
                table: "Rides");
        }
    }
}
