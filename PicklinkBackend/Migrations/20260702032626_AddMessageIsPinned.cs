using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageIsPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "MESSAGE", "isPinned", "bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0)");
            AddColumnIfMissing(migrationBuilder, "MATCH", "availableDateFrom", "date NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "availableDateTo", "date NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "maxSkillLevel", "int NOT NULL CONSTRAINT [DF_MATCH_maxSkillLevel] DEFAULT (5)");
            AddColumnIfMissing(migrationBuilder, "MATCH", "minSkillLevel", "int NOT NULL CONSTRAINT [DF_MATCH_minSkillLevel] DEFAULT (1)");
            AddColumnIfMissing(migrationBuilder, "MATCH", "province", "nvarchar(100) NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "searchLatitude", "float NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "searchLongitude", "float NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "searchRadiusKm", "float NOT NULL CONSTRAINT [DF_MATCH_searchRadiusKm] DEFAULT (5)");
            AddColumnIfMissing(migrationBuilder, "MATCH", "title", "nvarchar(200) NULL");
            AddColumnIfMissing(migrationBuilder, "MATCH", "ward", "nvarchar(150) NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropColumnIfExists(migrationBuilder, "MESSAGE", "isPinned");
            DropColumnIfExists(migrationBuilder, "MATCH", "availableDateFrom");
            DropColumnIfExists(migrationBuilder, "MATCH", "availableDateTo");
            DropColumnIfExists(migrationBuilder, "MATCH", "maxSkillLevel");
            DropColumnIfExists(migrationBuilder, "MATCH", "minSkillLevel");
            DropColumnIfExists(migrationBuilder, "MATCH", "province");
            DropColumnIfExists(migrationBuilder, "MATCH", "searchLatitude");
            DropColumnIfExists(migrationBuilder, "MATCH", "searchLongitude");
            DropColumnIfExists(migrationBuilder, "MATCH", "searchRadiusKm");
            DropColumnIfExists(migrationBuilder, "MATCH", "title");
            DropColumnIfExists(migrationBuilder, "MATCH", "ward");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string table, string column, string definition)
        {
            migrationBuilder.Sql($"""
                IF COL_LENGTH(N'{table}', N'{column}') IS NULL
                    ALTER TABLE [{table}] ADD [{column}] {definition};
                """);
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($"""
                IF COL_LENGTH(N'{table}', N'{column}') IS NOT NULL
                BEGIN
                    DECLARE @constraintName sysname;

                    SELECT @constraintName = [dc].[name]
                    FROM [sys].[default_constraints] AS [dc]
                    INNER JOIN [sys].[columns] AS [c] ON [c].[default_object_id] = [dc].[object_id]
                    INNER JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
                    WHERE [t].[name] = N'{table}' AND [c].[name] = N'{column}';

                    IF @constraintName IS NOT NULL
                        EXEC(N'ALTER TABLE [{table}] DROP CONSTRAINT ' + QUOTENAME(@constraintName));

                    ALTER TABLE [{table}] DROP COLUMN [{column}];
                END
                """);
        }
    }
}
