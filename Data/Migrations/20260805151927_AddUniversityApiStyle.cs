using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversityApiStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiStyle",
                table: "Universities",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiStyle",
                table: "Universities");
        }
    }
}
