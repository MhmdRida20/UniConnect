using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInternshipPostingModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalApplyUrl",
                table: "Internships",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalEmployerContactEmail",
                table: "Internships",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalEmployerLogoPath",
                table: "Internships",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalEmployerName",
                table: "Internships",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostingMode",
                table: "Internships",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalApplyUrl",
                table: "Internships");

            migrationBuilder.DropColumn(
                name: "ExternalEmployerContactEmail",
                table: "Internships");

            migrationBuilder.DropColumn(
                name: "ExternalEmployerLogoPath",
                table: "Internships");

            migrationBuilder.DropColumn(
                name: "ExternalEmployerName",
                table: "Internships");

            migrationBuilder.DropColumn(
                name: "PostingMode",
                table: "Internships");
        }
    }
}
