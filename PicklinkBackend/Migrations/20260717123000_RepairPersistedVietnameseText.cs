using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717123000_RepairPersistedVietnameseText")]
public partial class RepairPersistedVietnameseText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N'Thanh toÃƒÆ’Ã‚Â¡n cho booking ',
                N'Thanh toán cho booking ');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N' Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n.',
                N' đã được xác nhận.');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N' bÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ tÃƒÂ¡Ã‚Â»Ã‚Â« chÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœi: ',
                N' bị từ chối: ');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N'CÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n ',
                N'Cụm sân ');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N' Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c Admin duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t.',
                N' đã được Admin duyệt.');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N' Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚ÂºÃ‚Â¥p nhÃƒÂ¡Ã‚ÂºÃ‚Â­n lÃƒÂ¡Ã‚Â»Ã‚Âi mÃƒÂ¡Ã‚Â»Ã‚Âi tham gia trÃƒÂ¡Ã‚ÂºÃ‚Â­n ',
                N' đã chấp nhận lời mời tham gia trận ');

            UPDATE [NOTIFICATION_LOG]
            SET [message] = REPLACE(
                [message] COLLATE Latin1_General_100_BIN2,
                N' mÃƒÂ¡Ã‚Â»Ã‚Âi bÃƒÂ¡Ã‚ÂºÃ‚Â¡n tham gia trÃƒÂ¡Ã‚ÂºÃ‚Â­n ',
                N' mời bạn tham gia trận ');

            DECLARE @targets table (
                [tableName] sysname NOT NULL,
                [idColumn] sysname NOT NULL,
                [textColumn] sysname NOT NULL
            );

            INSERT INTO @targets VALUES
                (N'BOOKING_STATUS_HISTORY', N'bookingStatusHistoryId', N'reason'),
                (N'PAYMENT_STATUS_HISTORY', N'paymentStatusHistoryId', N'reason'),
                (N'NOTIFICATION_LOG', N'notifId', N'title'),
                (N'NOTIFICATION_LOG', N'notifId', N'message'),
                (N'NOTIFICATION_LOG', N'notifId', N'linkLabel'),
                (N'BOOKING', N'bookingId', N'title');

            DECLARE
                @tableName sysname,
                @idColumn sysname,
                @textColumn sysname,
                @sql nvarchar(max);

            DECLARE target_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [tableName], [idColumn], [textColumn] FROM @targets;

            OPEN target_cursor;
            FETCH NEXT FROM target_cursor INTO @tableName, @idColumn, @textColumn;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql =
                    N'DECLARE @iteration int = 0;
                      WHILE @iteration < 6
                      BEGIN
                          DROP TABLE IF EXISTS #DecodedText;
                          CREATE TABLE #DecodedText (
                              [id] bigint NOT NULL PRIMARY KEY,
                              [value] varchar(max) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL
                          );

                          INSERT INTO #DecodedText ([id], [value])
                          SELECT
                              CONVERT(bigint, source.' + QUOTENAME(@idColumn) + N'),
                              CONVERT(
                                  varbinary(max),
                                  CONVERT(
                                      varchar(max),
                                      source.' + QUOTENAME(@textColumn) + N'
                                          COLLATE Latin1_General_100_CI_AS))
                          FROM dbo.' + QUOTENAME(@tableName) + N' AS source
                          WHERE source.' + QUOTENAME(@textColumn) + N' IS NOT NULL
                            AND (
                                source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%Ã%''
                                OR source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%Ä%''
                                OR source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%Æ%''
                                OR source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%â€%''
                                OR source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%áº%''
                                OR source.' + QUOTENAME(@textColumn) + N' COLLATE Latin1_General_100_BIN2 LIKE N''%á»%''
                            );

                          DELETE FROM #DecodedText
                          WHERE CHARINDEX(
                              NCHAR(65533),
                              CONVERT(nvarchar(max), [value]) COLLATE Latin1_General_100_BIN2) > 0;

                          IF NOT EXISTS (SELECT 1 FROM #DecodedText) BREAK;

                          UPDATE source
                          SET ' + QUOTENAME(@textColumn) + N' =
                              CONVERT(nvarchar(max), decoded.[value])
                          FROM dbo.' + QUOTENAME(@tableName) + N' AS source
                          INNER JOIN #DecodedText AS decoded
                              ON decoded.[id] =
                                  CONVERT(bigint, source.' + QUOTENAME(@idColumn) + N');

                          SET @iteration += 1;
                      END;';

                EXEC sys.sp_executesql @sql;
                FETCH NEXT FROM target_cursor INTO @tableName, @idColumn, @textColumn;
            END;

            CLOSE target_cursor;
            DEALLOCATE target_cursor;
            DROP TABLE IF EXISTS #DecodedText;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Text repair is intentionally irreversible.
    }
}