using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideUniversityScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "Rides",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Rides_UniversityCode",
                table: "Rides",
                column: "UniversityCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Rides_Universities_UniversityCode",
                table: "Rides",
                column: "UniversityCode",
                principalTable: "Universities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rides_Universities_UniversityCode",
                table: "Rides");

            migrationBuilder.DropIndex(
                name: "IX_Rides_UniversityCode",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "Rides");
        }
    }
}
