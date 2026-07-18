using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistExternalSimData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalSimCourses",
                columns: table => new
                {
                    ApiKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CourseName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InstructorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InstructorStaffId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Credits = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSimCourses", x => new { x.ApiKey, x.CourseCode });
                });

            migrationBuilder.CreateTable(
                name: "ExternalSimEnrollments",
                columns: table => new
                {
                    ApiKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StudentNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSimEnrollments", x => new { x.ApiKey, x.StudentNumber, x.CourseCode });
                });

            migrationBuilder.CreateTable(
                name: "ExternalSimStudents",
                columns: table => new
                {
                    ApiKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StudentNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Major = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    YearOfStudy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSimStudents", x => new { x.ApiKey, x.StudentNumber });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalSimCourses");

            migrationBuilder.DropTable(
                name: "ExternalSimEnrollments");

            migrationBuilder.DropTable(
                name: "ExternalSimStudents");
        }
    }
}
