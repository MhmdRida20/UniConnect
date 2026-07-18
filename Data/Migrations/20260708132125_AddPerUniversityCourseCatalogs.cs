using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerUniversityCourseCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_Courses_CourseCode",
                table: "Enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Courses_CourseCode",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_CourseCode",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_Enrollments_CourseCode",
                table: "Enrollments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Courses",
                table: "Courses");

            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "StudyGroups",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "Enrollments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UniversityCode",
                table: "Courses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Courses",
                table: "Courses",
                columns: new[] { "UniversityCode", "CourseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_UniversityCode_CourseCode",
                table: "StudyGroups",
                columns: new[] { "UniversityCode", "CourseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_UniversityCode_CourseCode",
                table: "Enrollments",
                columns: new[] { "UniversityCode", "CourseCode" });

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Universities_UniversityCode",
                table: "Courses",
                column: "UniversityCode",
                principalTable: "Universities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_Courses_UniversityCode_CourseCode",
                table: "Enrollments",
                columns: new[] { "UniversityCode", "CourseCode" },
                principalTable: "Courses",
                principalColumns: new[] { "UniversityCode", "CourseCode" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Courses_UniversityCode_CourseCode",
                table: "StudyGroups",
                columns: new[] { "UniversityCode", "CourseCode" },
                principalTable: "Courses",
                principalColumns: new[] { "UniversityCode", "CourseCode" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Universities_UniversityCode",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_Courses_UniversityCode_CourseCode",
                table: "Enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Courses_UniversityCode_CourseCode",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_UniversityCode_CourseCode",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_Enrollments_UniversityCode_CourseCode",
                table: "Enrollments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Courses",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "UniversityCode",
                table: "Courses");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Courses",
                table: "Courses",
                column: "CourseCode");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_CourseCode",
                table: "StudyGroups",
                column: "CourseCode");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_CourseCode",
                table: "Enrollments",
                column: "CourseCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_Courses_CourseCode",
                table: "Enrollments",
                column: "CourseCode",
                principalTable: "Courses",
                principalColumn: "CourseCode",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Courses_CourseCode",
                table: "StudyGroups",
                column: "CourseCode",
                principalTable: "Courses",
                principalColumn: "CourseCode",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
