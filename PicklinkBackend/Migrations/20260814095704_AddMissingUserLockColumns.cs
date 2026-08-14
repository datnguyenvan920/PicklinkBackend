using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingUserLockColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns are mapped by the EF model but were only ever created by the
            // raw SQL in Startup/SchemaStartup.cs, which runs solely when
            // Startup:RunSchemaChecks is true. Databases that never ran with that flag on
            // are missing them, so loading a User entity fails there. The guards keep this
            // migration a no-op on databases where the schema check already added them.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'USER', N'lockedAt') IS NULL
                    ALTER TABLE [USER] ADD [lockedAt] datetime NULL;
                IF COL_LENGTH(N'USER', N'lockedByUserId') IS NULL
                    ALTER TABLE [USER] ADD [lockedByUserId] int NULL;
                IF COL_LENGTH(N'USER', N'unlockedAt') IS NULL
                    ALTER TABLE [USER] ADD [unlockedAt] datetime NULL;
                IF COL_LENGTH(N'USER', N'unlockedByUserId') IS NULL
                    ALTER TABLE [USER] ADD [unlockedByUserId] int NULL;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USER_LOCKED_BY')
                    ALTER TABLE [USER] ADD CONSTRAINT [FK_USER_LOCKED_BY]
                    FOREIGN KEY ([lockedByUserId]) REFERENCES [USER]([userId]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USER_UNLOCKED_BY')
                    ALTER TABLE [USER] ADD CONSTRAINT [FK_USER_UNLOCKED_BY]
                    FOREIGN KEY ([unlockedByUserId]) REFERENCES [USER]([userId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USER_lockedByUserId' AND object_id = OBJECT_ID(N'[USER]'))
                    CREATE INDEX [IX_USER_lockedByUserId] ON [USER] ([lockedByUserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USER_unlockedByUserId' AND object_id = OBJECT_ID(N'[USER]'))
                    CREATE INDEX [IX_USER_unlockedByUserId] ON [USER] ([unlockedByUserId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USER_unlockedByUserId' AND object_id = OBJECT_ID(N'[USER]'))
                    DROP INDEX [IX_USER_unlockedByUserId] ON [USER];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USER_lockedByUserId' AND object_id = OBJECT_ID(N'[USER]'))
                    DROP INDEX [IX_USER_lockedByUserId] ON [USER];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USER_UNLOCKED_BY')
                    ALTER TABLE [USER] DROP CONSTRAINT [FK_USER_UNLOCKED_BY];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USER_LOCKED_BY')
                    ALTER TABLE [USER] DROP CONSTRAINT [FK_USER_LOCKED_BY];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'USER', N'unlockedByUserId') IS NOT NULL
                    ALTER TABLE [USER] DROP COLUMN [unlockedByUserId];
                IF COL_LENGTH(N'USER', N'unlockedAt') IS NOT NULL
                    ALTER TABLE [USER] DROP COLUMN [unlockedAt];
                IF COL_LENGTH(N'USER', N'lockedByUserId') IS NOT NULL
                    ALTER TABLE [USER] DROP COLUMN [lockedByUserId];
                IF COL_LENGTH(N'USER', N'lockedAt') IS NOT NULL
                    ALTER TABLE [USER] DROP COLUMN [lockedAt];
                """);
        }
    }
}
