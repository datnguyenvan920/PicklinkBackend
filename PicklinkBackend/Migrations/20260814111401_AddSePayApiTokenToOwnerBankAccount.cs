using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddSePayApiTokenToOwnerBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Holds each venue owner's SePay Secret API token, encrypted by IEncryptionService,
            // so reconciliation can query SePay with the account the money actually lands in.
            // The guard keeps this a no-op on databases where Startup/SchemaStartup.cs already
            // added the column (it runs only when Startup:RunSchemaChecks is true).
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'OWNER_BANK_ACCOUNT', N'sePayApiToken') IS NULL
                    ALTER TABLE [OWNER_BANK_ACCOUNT] ADD [sePayApiToken] nvarchar(500) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'OWNER_BANK_ACCOUNT', N'sePayApiToken') IS NOT NULL
                    ALTER TABLE [OWNER_BANK_ACCOUNT] DROP COLUMN [sePayApiToken];
                """);
        }
    }
}
