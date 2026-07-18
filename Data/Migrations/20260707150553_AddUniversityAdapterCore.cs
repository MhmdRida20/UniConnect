using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversityAdapterCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "Students",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Universities",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AdapterMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Universities", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Students_UniversityCode",
                table: "Students",
                column: "UniversityCode");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UniversityCode",
                table: "AspNetUsers",
                column: "UniversityCode");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Universities_UniversityCode",
                table: "AspNetUsers",
                column: "UniversityCode",
                principalTable: "Universities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Universities_UniversityCode",
                table: "Students",
                column: "UniversityCode",
                principalTable: "Universities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Universities_UniversityCode",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Universities_UniversityCode",
                table: "Students");

            migrationBuilder.DropTable(
                name: "Universities");

            migrationBuilder.DropIndex(
                name: "IX_Students_UniversityCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_UniversityCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "AspNetUsers");
        }
    }
}
