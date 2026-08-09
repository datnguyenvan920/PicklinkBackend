using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260809163000_AddMatchOrigin")]
public partial class AddMatchOrigin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'MATCH', N'origin') IS NULL
            BEGIN
                ALTER TABLE [MATCH] ADD [origin] nvarchar(20) NOT NULL
                    CONSTRAINT [DF_MATCH_origin] DEFAULT (N'Community');
                EXEC sp_executesql N'
                    UPDATE target
                    SET target.[origin] = N''Manual''
                    FROM [MATCH] target
                    WHERE EXISTS (
                        SELECT 1
                        FROM [MATCHMAKING_QUEUE] queue
                        WHERE queue.[matchId] = target.[matchId]
                          AND queue.[isPublic] = 1
                    );
                ';
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'MATCH', N'origin') IS NOT NULL
            BEGIN
                DECLARE @constraintName sysname;
                SELECT @constraintName = defaults.[name]
                FROM sys.default_constraints defaults
                INNER JOIN sys.columns columns
                    ON columns.[default_object_id] = defaults.[object_id]
                WHERE defaults.[parent_object_id] = OBJECT_ID(N'[MATCH]')
                  AND columns.[name] = N'origin';

                IF @constraintName IS NOT NULL
                    EXEC(N'ALTER TABLE [MATCH] DROP CONSTRAINT [' + @constraintName + N']');

                ALTER TABLE [MATCH] DROP COLUMN [origin];
            END
            """);
    }
}
