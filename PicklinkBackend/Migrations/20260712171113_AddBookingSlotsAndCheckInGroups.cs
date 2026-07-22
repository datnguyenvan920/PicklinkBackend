using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSlotsAndCheckInGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[USER]') AND name = 'isLocked')
BEGIN ALTER TABLE [USER] ADD [isLocked] bit NOT NULL DEFAULT CAST(0 AS bit); END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[RATING_HISTORY]') AND name = 'isHidden')
BEGIN ALTER TABLE [RATING_HISTORY] ADD [isHidden] bit NOT NULL DEFAULT CAST(0 AS bit); END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[RATING_HISTORY]') AND name = 'moderatedAt')
BEGIN ALTER TABLE [RATING_HISTORY] ADD [moderatedAt] datetime NULL; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[RATING_HISTORY]') AND name = 'moderatedByUserId')
BEGIN ALTER TABLE [RATING_HISTORY] ADD [moderatedByUserId] int NULL; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[RATING_HISTORY]') AND name = 'moderationNote')
BEGIN ALTER TABLE [RATING_HISTORY] ADD [moderationNote] nvarchar(1000) NULL; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[RATING_HISTORY]') AND name = 'moderationStatus')
BEGIN ALTER TABLE [RATING_HISTORY] ADD [moderationStatus] nvarchar(30) NOT NULL DEFAULT 'Visible'; END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'createdAt')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [createdAt] datetime2 NOT NULL DEFAULT (getutcdate()); END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'linkLabel')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [linkLabel] nvarchar(100) NULL; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'linkTo')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [linkTo] nvarchar(500) NULL; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'notificationType')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [notificationType] nvarchar(30) NOT NULL DEFAULT 'system'; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'title')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [title] nvarchar(200) NOT NULL DEFAULT N'Thông báo'; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[NOTIFICATION_LOG]') AND name = 'tone')
BEGIN ALTER TABLE [NOTIFICATION_LOG] ADD [tone] nvarchar(20) NOT NULL DEFAULT 'default'; END

IF OBJECT_ID(N'[BOOKING_CHECKIN_GROUP]', N'U') IS NULL
BEGIN
    CREATE TABLE [BOOKING_CHECKIN_GROUP] (
        [bookingCheckInGroupId] int NOT NULL IDENTITY,
        [bookingId] int NOT NULL,
        [courtId] int NOT NULL,
        [startTime] datetime NOT NULL,
        [endTime] datetime NOT NULL,
        [checkInCode] nvarchar(30) NOT NULL,
        [checkInStatus] nvarchar(30) NOT NULL DEFAULT N'Ready',
        [codeVerifiedAt] datetime NULL,
        [codeVerifiedByUserId] int NULL,
        [checkedInAt] datetime NULL,
        [checkedInByUserId] int NULL,
        [noShowAt] datetime NULL,
        [noShowByUserId] int NULL,
        [updatedAt] datetime NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_BOOKING_CHECKIN_GROUP] PRIMARY KEY ([bookingCheckInGroupId]),
        CONSTRAINT [FK_BOOKING_CHECKIN_GROUP_BOOKING_bookingId] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE CASCADE,
        CONSTRAINT [FK_BOOKING_CHECKIN_GROUP_COURT_courtId] FOREIGN KEY ([courtId]) REFERENCES [COURT] ([courtId]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[COMMUNITY_REPORT]', N'U') IS NULL
BEGIN
    CREATE TABLE [COMMUNITY_REPORT] (
        [communityReportId] int NOT NULL IDENTITY,
        [reporterUserId] int NOT NULL,
        [targetType] nvarchar(50) NOT NULL,
        [targetId] int NULL,
        [targetLabel] nvarchar(250) NOT NULL,
        [reason] nvarchar(200) NOT NULL,
        [description] nvarchar(2000) NULL,
        [status] nvarchar(30) NOT NULL DEFAULT N'Open',
        [priority] nvarchar(30) NOT NULL DEFAULT N'Normal',
        [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
        [reviewedAt] datetime NULL,
        [reviewedByUserId] int NULL,
        [resolutionNote] nvarchar(1000) NULL,
        CONSTRAINT [PK_COMMUNITY_REPORT] PRIMARY KEY ([communityReportId]),
        CONSTRAINT [FK_COMMUNITY_REPORT_REPORTER] FOREIGN KEY ([reporterUserId]) REFERENCES [USER] ([userId]),
        CONSTRAINT [FK_COMMUNITY_REPORT_REVIEWER] FOREIGN KEY ([reviewedByUserId]) REFERENCES [USER] ([userId])
    );
END

IF OBJECT_ID(N'[LISTING_FEE_SETTING]', N'U') IS NULL
BEGIN
    CREATE TABLE [LISTING_FEE_SETTING] (
        [listingFeeSettingId] int NOT NULL IDENTITY,
        [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
        [updatedAt] datetime NOT NULL DEFAULT ((getutcdate())),
        [updatedByUserId] int NULL,
        CONSTRAINT [PK_LISTING_FEE_SETTING] PRIMARY KEY ([listingFeeSettingId]),
        CONSTRAINT [FK_LISTING_FEE_SETTING_USER] FOREIGN KEY ([updatedByUserId]) REFERENCES [USER] ([userId])
    );
END

IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL
BEGIN
    CREATE TABLE [MATCH_SLOT_VOTE] (
        [matchSlotVoteId] int NOT NULL IDENTITY,
        [matchId] int NOT NULL,
        [playerId] int NOT NULL,
        [courtId] int NOT NULL,
        [startTime] datetime NOT NULL,
        [endTime] datetime NOT NULL,
        [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_MATCH_SLOT_VOTE] PRIMARY KEY ([matchSlotVoteId]),
        CONSTRAINT [CK_MATCH_SLOT_VOTE_time] CHECK ([endTime] > [startTime]),
        CONSTRAINT [FK_MATCH_SLOT_VOTE_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT] ([courtId]),
        CONSTRAINT [FK_MATCH_SLOT_VOTE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]) ON DELETE CASCADE,
        CONSTRAINT [FK_MATCH_SLOT_VOTE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[PLATFORM_SETTING]', N'U') IS NULL
BEGIN
    CREATE TABLE [PLATFORM_SETTING] (
        [platformSettingId] int NOT NULL IDENTITY,
        [settingKey] nvarchar(100) NOT NULL,
        [settingValue] nvarchar(500) NOT NULL,
        [settingGroup] nvarchar(100) NOT NULL DEFAULT N'General',
        [description] nvarchar(500) NOT NULL DEFAULT N'',
        [updatedAt] datetime NOT NULL DEFAULT ((getutcdate())),
        [updatedByUserId] int NULL,
        CONSTRAINT [PK_PLATFORM_SETTING] PRIMARY KEY ([platformSettingId]),
        CONSTRAINT [FK_PLATFORM_SETTING_UPDATED_BY] FOREIGN KEY ([updatedByUserId]) REFERENCES [USER] ([userId])
    );
END

IF OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U') IS NULL
BEGIN
    CREATE TABLE [VENUE_LISTING_PAYMENT] (
        [venueListingPaymentId] int NOT NULL IDENTITY,
        [venueId] int NOT NULL,
        [months] int NOT NULL,
        [activeCourtCount] int NOT NULL,
        [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
        [amount] decimal(18,2) NOT NULL,
        [status] nvarchar(30) NOT NULL,
        [receiptImageUrl] nvarchar(1000) NULL,
        [rejectionReason] nvarchar(500) NULL,
        [submittedAt] datetime NOT NULL DEFAULT ((getutcdate())),
        [reviewedAt] datetime NULL,
        [reviewedByUserId] int NULL,
        [paidFrom] datetime NULL,
        [paidUntil] datetime NULL,
        CONSTRAINT [PK_VENUE_LISTING_PAYMENT] PRIMARY KEY ([venueListingPaymentId]),
        CONSTRAINT [FK_VENUE_LISTING_PAYMENT_REVIEWER] FOREIGN KEY ([reviewedByUserId]) REFERENCES [USER] ([userId]),
        CONSTRAINT [FK_VENUE_LISTING_PAYMENT_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[BOOKING_SLOT]', N'U') IS NULL
BEGIN
    CREATE TABLE [BOOKING_SLOT] (
        [bookingSlotId] int NOT NULL IDENTITY,
        [bookingId] int NOT NULL,
        [courtId] int NOT NULL,
        [checkInGroupId] int NULL,
        [startTime] datetime NOT NULL,
        [endTime] datetime NOT NULL,
        [hourlyPriceSnapshot] float NOT NULL,
        [courtAmount] float NOT NULL,
        CONSTRAINT [PK_BOOKING_SLOT] PRIMARY KEY ([bookingSlotId]),
        CONSTRAINT [FK_BOOKING_SLOT_BOOKING_CHECKIN_GROUP_checkInGroupId] FOREIGN KEY ([checkInGroupId]) REFERENCES [BOOKING_CHECKIN_GROUP] ([bookingCheckInGroupId]),
        CONSTRAINT [FK_BOOKING_SLOT_BOOKING_bookingId] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE CASCADE,
        CONSTRAINT [FK_BOOKING_SLOT_COURT_courtId] FOREIGN KEY ([courtId]) REFERENCES [COURT] ([courtId]) ON DELETE NO ACTION
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RATING_HISTORY_moderatedByUserId' AND object_id = OBJECT_ID('RATING_HISTORY'))
    CREATE INDEX [IX_RATING_HISTORY_moderatedByUserId] ON [RATING_HISTORY] ([moderatedByUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BOOKING_CHECKIN_GROUP_booking_time' AND object_id = OBJECT_ID('BOOKING_CHECKIN_GROUP'))
    CREATE INDEX [IX_BOOKING_CHECKIN_GROUP_booking_time] ON [BOOKING_CHECKIN_GROUP] ([bookingId], [startTime]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BOOKING_CHECKIN_GROUP_courtId' AND object_id = OBJECT_ID('BOOKING_CHECKIN_GROUP'))
    CREATE INDEX [IX_BOOKING_CHECKIN_GROUP_courtId] ON [BOOKING_CHECKIN_GROUP] ([courtId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_BOOKING_CHECKIN_GROUP_code' AND object_id = OBJECT_ID('BOOKING_CHECKIN_GROUP'))
    CREATE UNIQUE INDEX [UQ_BOOKING_CHECKIN_GROUP_code] ON [BOOKING_CHECKIN_GROUP] ([checkInCode]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BOOKING_SLOT_booking_time' AND object_id = OBJECT_ID('BOOKING_SLOT'))
    CREATE INDEX [IX_BOOKING_SLOT_booking_time] ON [BOOKING_SLOT] ([bookingId], [startTime]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BOOKING_SLOT_checkInGroupId' AND object_id = OBJECT_ID('BOOKING_SLOT'))
    CREATE INDEX [IX_BOOKING_SLOT_checkInGroupId] ON [BOOKING_SLOT] ([checkInGroupId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BOOKING_SLOT_court_time' AND object_id = OBJECT_ID('BOOKING_SLOT'))
    CREATE INDEX [IX_BOOKING_SLOT_court_time] ON [BOOKING_SLOT] ([courtId], [startTime], [endTime]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_COMMUNITY_REPORT_reporterUserId' AND object_id = OBJECT_ID('COMMUNITY_REPORT'))
    CREATE INDEX [IX_COMMUNITY_REPORT_reporterUserId] ON [COMMUNITY_REPORT] ([reporterUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_COMMUNITY_REPORT_reviewedByUserId' AND object_id = OBJECT_ID('COMMUNITY_REPORT'))
    CREATE INDEX [IX_COMMUNITY_REPORT_reviewedByUserId] ON [COMMUNITY_REPORT] ([reviewedByUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_COMMUNITY_REPORT_status' AND object_id = OBJECT_ID('COMMUNITY_REPORT'))
    CREATE INDEX [IX_COMMUNITY_REPORT_status] ON [COMMUNITY_REPORT] ([status]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_COMMUNITY_REPORT_targetType' AND object_id = OBJECT_ID('COMMUNITY_REPORT'))
    CREATE INDEX [IX_COMMUNITY_REPORT_targetType] ON [COMMUNITY_REPORT] ([targetType]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LISTING_FEE_SETTING_updatedByUserId' AND object_id = OBJECT_ID('LISTING_FEE_SETTING'))
    CREATE INDEX [IX_LISTING_FEE_SETTING_updatedByUserId] ON [LISTING_FEE_SETTING] ([updatedByUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MATCH_SLOT_VOTE_court_time' AND object_id = OBJECT_ID('MATCH_SLOT_VOTE'))
    CREATE INDEX [IX_MATCH_SLOT_VOTE_court_time] ON [MATCH_SLOT_VOTE] ([courtId], [startTime], [endTime]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MATCH_SLOT_VOTE_matchId' AND object_id = OBJECT_ID('MATCH_SLOT_VOTE'))
    CREATE INDEX [IX_MATCH_SLOT_VOTE_matchId] ON [MATCH_SLOT_VOTE] ([matchId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MATCH_SLOT_VOTE_playerId' AND object_id = OBJECT_ID('MATCH_SLOT_VOTE'))
    CREATE INDEX [IX_MATCH_SLOT_VOTE_playerId] ON [MATCH_SLOT_VOTE] ([playerId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MATCH_SLOT_VOTE_player_slot' AND object_id = OBJECT_ID('MATCH_SLOT_VOTE'))
    CREATE UNIQUE INDEX [UQ_MATCH_SLOT_VOTE_player_slot] ON [MATCH_SLOT_VOTE] ([matchId], [playerId], [courtId], [startTime], [endTime]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PLATFORM_SETTING_updatedByUserId' AND object_id = OBJECT_ID('PLATFORM_SETTING'))
    CREATE INDEX [IX_PLATFORM_SETTING_updatedByUserId] ON [PLATFORM_SETTING] ([updatedByUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_PLATFORM_SETTING_settingKey' AND object_id = OBJECT_ID('PLATFORM_SETTING'))
    CREATE UNIQUE INDEX [UQ_PLATFORM_SETTING_settingKey] ON [PLATFORM_SETTING] ([settingKey]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VENUE_LISTING_PAYMENT_reviewedByUserId' AND object_id = OBJECT_ID('VENUE_LISTING_PAYMENT'))
    CREATE INDEX [IX_VENUE_LISTING_PAYMENT_reviewedByUserId] ON [VENUE_LISTING_PAYMENT] ([reviewedByUserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VENUE_LISTING_PAYMENT_status' AND object_id = OBJECT_ID('VENUE_LISTING_PAYMENT'))
    CREATE INDEX [IX_VENUE_LISTING_PAYMENT_status] ON [VENUE_LISTING_PAYMENT] ([status]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VENUE_LISTING_PAYMENT_venueId' AND object_id = OBJECT_ID('VENUE_LISTING_PAYMENT'))
    CREATE INDEX [IX_VENUE_LISTING_PAYMENT_venueId] ON [VENUE_LISTING_PAYMENT] ([venueId]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RATING_HISTORY_MODERATOR')
    ALTER TABLE [RATING_HISTORY] ADD CONSTRAINT [FK_RATING_HISTORY_MODERATOR] FOREIGN KEY ([moderatedByUserId]) REFERENCES [USER] ([userId]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RATING_HISTORY_MODERATOR",
                table: "RATING_HISTORY");

            migrationBuilder.DropTable(
                name: "BOOKING_SLOT");

            migrationBuilder.DropTable(
                name: "COMMUNITY_REPORT");

            migrationBuilder.DropTable(
                name: "LISTING_FEE_SETTING");

            migrationBuilder.DropTable(
                name: "MATCH_SLOT_VOTE");

            migrationBuilder.DropTable(
                name: "PLATFORM_SETTING");

            migrationBuilder.DropTable(
                name: "VENUE_LISTING_PAYMENT");

            migrationBuilder.DropTable(
                name: "BOOKING_CHECKIN_GROUP");

            migrationBuilder.DropIndex(
                name: "IX_RATING_HISTORY_moderatedByUserId",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "isLocked",
                table: "USER");

            migrationBuilder.DropColumn(
                name: "isHidden",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderatedAt",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderatedByUserId",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderationNote",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderationStatus",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "createdAt",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "linkLabel",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "linkTo",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "notificationType",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "title",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "tone",
                table: "NOTIFICATION_LOG");
        }
    }
}
