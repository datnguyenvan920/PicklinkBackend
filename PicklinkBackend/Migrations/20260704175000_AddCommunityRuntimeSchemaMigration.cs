using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260704175000_AddCommunityRuntimeSchemaMigration")]
    public partial class AddCommunityRuntimeSchemaMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[SOCIAL_GROUP]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'SOCIAL_GROUP', N'activeLocation') IS NULL
                BEGIN
                    ALTER TABLE [SOCIAL_GROUP] ADD [activeLocation] nvarchar(255) NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[POST_COMMENT_LIKE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [POST_COMMENT_LIKE] (
                        [commentLikeId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_COMMENT_LIKE] PRIMARY KEY,
                        [commentId] int NOT NULL,
                        [userId] int NOT NULL,
                        [createdAt] datetime2 NOT NULL CONSTRAINT [DF_POST_COMMENT_LIKE_createdAt] DEFAULT (sysutcdatetime()),
                        CONSTRAINT [FK_POST_COMMENT_LIKE_COMMENT] FOREIGN KEY ([commentId]) REFERENCES [POST_COMMENT]([commentId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_POST_COMMENT_LIKE_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId]),
                        CONSTRAINT [UQ_POST_COMMENT_LIKE_commentId_userId] UNIQUE ([commentId], [userId])
                    );
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration adopts schema that may already have been created by
            // legacy startup/controller DDL. Rolling it back cannot distinguish
            // migration-created objects from pre-existing production data, so the
            // rollback is intentionally non-destructive.
        }
    }
}
