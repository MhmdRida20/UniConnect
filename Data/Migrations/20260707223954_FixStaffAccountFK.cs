using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixStaffAccountFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Students_UniversityId",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Students_UniversityId",
                table: "AspNetUsers",
                column: "UniversityId",
                principalTable: "Students",
                principalColumn: "UniversityId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
