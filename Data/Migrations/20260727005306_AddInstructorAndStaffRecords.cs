using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstructorAndStaffRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstructorEmail",
                table: "ExternalSimCourses",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalSimStaff",
                columns: table => new
                {
                    ApiKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSimStaff", x => new { x.ApiKey, x.StaffId });
                });

            migrationBuilder.CreateTable(
                name: "Instructors",
                columns: table => new
                {
                    StaffId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UniversityCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UniversityEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instructors", x => x.StaffId);
                    table.ForeignKey(
                        name: "FK_Instructors_Universities_UniversityCode",
                        column: x => x.UniversityCode,
                        principalTable: "Universities",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffRecords",
                columns: table => new
                {
                    StaffId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UniversityCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UniversityEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRecords", x => x.StaffId);
                    table.ForeignKey(
                        name: "FK_StaffRecords_Universities_UniversityCode",
                        column: x => x.UniversityCode,
                        principalTable: "Universities",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Instructors_UniversityCode",
                table: "Instructors",
                column: "UniversityCode");

            migrationBuilder.CreateIndex(
                name: "IX_StaffRecords_UniversityCode",
                table: "StaffRecords",
                column: "UniversityCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalSimStaff");

            migrationBuilder.DropTable(
                name: "Instructors");

            migrationBuilder.DropTable(
                name: "StaffRecords");

            migrationBuilder.DropColumn(
                name: "InstructorEmail",
                table: "ExternalSimCourses");
        }
    }
}
