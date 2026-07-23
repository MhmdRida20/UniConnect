using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversitySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UniversitySettings",
                columns: table => new
                {
                    UniversityCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaxStudyGroupMembers = table.Column<int>(type: "int", nullable: false),
                    DefaultAttendanceGpsRadiusMeters = table.Column<int>(type: "int", nullable: false),
                    DefaultAttendanceGraceMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxClubMembers = table.Column<int>(type: "int", nullable: true),
                    MaxRideRequestsPerWindow = table.Column<int>(type: "int", nullable: false),
                    RideRequestWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniversitySettings", x => x.UniversityCode);
                    table.ForeignKey(
                        name: "FK_UniversitySettings_Universities_UniversityCode",
                        column: x => x.UniversityCode,
                        principalTable: "Universities",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UniversitySettings");
        }
    }
}
