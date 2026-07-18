using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedSkillsNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentSkills_CareerProfiles_CareerProfileId",
                table: "StudentSkills");

            migrationBuilder.DropIndex(
                name: "IX_StudentSkills_CareerProfileId",
                table: "StudentSkills");

            migrationBuilder.DropColumn(
                name: "CareerProfileId",
                table: "StudentSkills");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CareerProfileId",
                table: "StudentSkills",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSkills_CareerProfileId",
                table: "StudentSkills",
                column: "CareerProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentSkills_CareerProfiles_CareerProfileId",
                table: "StudentSkills",
                column: "CareerProfileId",
                principalTable: "CareerProfiles",
                principalColumn: "Id");
        }
    }
}
