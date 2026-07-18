using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniversityPostsInternships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Companies");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalEmployerName",
                table: "Internships",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalApplyEmail",
                table: "Internships",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_UniversityCode",
                table: "Companies",
                column: "UniversityCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Universities_UniversityCode",
                table: "Companies",
                column: "UniversityCode",
                principalTable: "Universities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Universities_UniversityCode",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_UniversityCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ExternalApplyEmail",
                table: "Internships");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "Companies");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalEmployerName",
                table: "Internships",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Companies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Companies",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }
    }
}
