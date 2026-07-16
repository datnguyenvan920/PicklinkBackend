using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations;

public partial class ConvertMoneyToDecimal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AlterMoneyColumns<decimal>(migrationBuilder, "decimal(18,2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        AlterMoneyColumns<double>(migrationBuilder, "float");
    }

    private static void AlterMoneyColumns<T>(MigrationBuilder migrationBuilder, string type)
    {
        migrationBuilder.AlterColumn<T>("hourlyPriceSnapshot", "BOOKING", type, nullable: false);
        migrationBuilder.AlterColumn<T>("courtAmount", "BOOKING", type, nullable: false);
        migrationBuilder.AlterColumn<T>("totalAmount", "BOOKING", type, nullable: false);
        migrationBuilder.AlterColumn<T>("hourlyPriceSnapshot", "BOOKING_SLOT", type, nullable: false);
        migrationBuilder.AlterColumn<T>("courtAmount", "BOOKING_SLOT", type, nullable: false);
        migrationBuilder.AlterColumn<T>("hourlyPrice", "COURT", type, nullable: false);
        migrationBuilder.AlterColumn<T>("pricePerUnit", "INVENTORY_ITEM", type, nullable: false);
        migrationBuilder.AlterColumn<T>("amount", "PAYMENT", type, nullable: false);
    }
}