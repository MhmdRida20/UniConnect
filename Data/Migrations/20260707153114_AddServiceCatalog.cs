using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IconClass = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsImplemented = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "UniversityServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UniversityCode = table.Column<string>(type: "nvarchar(20)", nullable: false),
                    ServiceCode = table.Column<string>(type: "nvarchar(30)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniversityServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UniversityServices_Services_ServiceCode",
                        column: x => x.ServiceCode,
                        principalTable: "Services",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UniversityServices_Universities_UniversityCode",
                        column: x => x.UniversityCode,
                        principalTable: "Universities",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UniversityServices_ServiceCode",
                table: "UniversityServices",
                column: "ServiceCode");

            migrationBuilder.CreateIndex(
                name: "IX_UniversityServices_UniversityCode_ServiceCode",
                table: "UniversityServices",
                columns: new[] { "UniversityCode", "ServiceCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UniversityServices");

            migrationBuilder.DropTable(
                name: "Services");
        }
    }
}
