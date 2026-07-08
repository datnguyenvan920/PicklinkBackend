using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260708145501_AddAdministrativeLocations")]
    public partial class AddAdministrativeLocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Provinces",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(130)", maxLength: 130, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provinces", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Wards",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProvinceCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wards", x => x.Code);
                    table.ForeignKey(
                        name: "FK_Wards_Provinces_ProvinceCode",
                        column: x => x.ProvinceCode,
                        principalTable: "Provinces",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wards_ProvinceCode",
                table: "Wards",
                column: "ProvinceCode");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wards");

            migrationBuilder.DropTable(
                name: "Provinces");
        }
    }
}