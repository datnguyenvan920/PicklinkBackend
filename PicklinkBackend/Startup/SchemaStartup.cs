using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Startup;

internal static class SchemaStartup
{
    internal static void RunSchemaChecks(this WebApplication app)
    {
        EnsurePasswordResetSchema(app);
        EnsureAdminUserSchema(app);
        EnsureUserProfileSchema(app);
        EnsurePlayerProfileSchema(app);
        EnsureCommunitySchema(app);
        EnsureCommunityReportSchema(app);
        EnsureAdminReviewSchema(app);
        EnsureAdminSettingsSchema(app);
        EnsureOwnerVenueSchema(app);
        EnsureListingFeeSchema(app);
        EnsurePaymentSchema(app);
        EnsureStaffOperationSchema(app);
        EnsureBookingSlotSchema(app);
        EnsurePlayerPhase7Schema(app);
        EnsurePlayerMatchSchema(app);
        EnsureLocationSchema(app);
        EnsureForeignKeyIndexes(app);
    }

    private static void EnsureForeignKeyIndexes(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_CONVERSATION_matchId' AND [object_id] = OBJECT_ID(N'[CONVERSATION]'))
                CREATE INDEX [IX_CONVERSATION_matchId] ON [CONVERSATION] ([matchId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_LISTING_FEE_SETTING_updatedByUserId' AND [object_id] = OBJECT_ID(N'[LISTING_FEE_SETTING]'))
                CREATE INDEX [IX_LISTING_FEE_SETTING_updatedByUserId] ON [LISTING_FEE_SETTING] ([updatedByUserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_hostPlayerId' AND [object_id] = OBJECT_ID(N'[MATCH]'))
                CREATE INDEX [IX_MATCH_hostPlayerId] ON [MATCH] ([hostPlayerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_PLAYER_REVIEW_reviewerPlayerId' AND [object_id] = OBJECT_ID(N'[MATCH_PLAYER_REVIEW]'))
                CREATE INDEX [IX_MATCH_PLAYER_REVIEW_reviewerPlayerId] ON [MATCH_PLAYER_REVIEW] ([reviewerPlayerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_SLOT_VOTE_playerId' AND [object_id] = OBJECT_ID(N'[MATCH_SLOT_VOTE]'))
                CREATE INDEX [IX_MATCH_SLOT_VOTE_playerId] ON [MATCH_SLOT_VOTE] ([playerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_POST_COMMENT_LIKE_userId' AND [object_id] = OBJECT_ID(N'[POST_COMMENT_LIKE]'))
                CREATE INDEX [IX_POST_COMMENT_LIKE_userId] ON [POST_COMMENT_LIKE] ([userId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_VENUE_LISTING_PAYMENT_reviewedByUserId' AND [object_id] = OBJECT_ID(N'[VENUE_LISTING_PAYMENT]'))
                CREATE INDEX [IX_VENUE_LISTING_PAYMENT_reviewedByUserId] ON [VENUE_LISTING_PAYMENT] ([reviewedByUserId]);
            """);
    }

    private const int ExpectedProvinceCount = 34;
    private const int ExpectedWardCount = 3321;
    private const string ExpectedFirstProvinceCode = "P001";
    private const string ExpectedFirstWardCode = "P001-W001";

    private static void EnsureLocationSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[Provinces]', N'U') IS NULL
            BEGIN
                CREATE TABLE [Provinces] (
                    [Code] nvarchar(10) NOT NULL CONSTRAINT [PK_Provinces] PRIMARY KEY,
                    [Name] nvarchar(100) NOT NULL,
                    [FullName] nvarchar(130) NOT NULL
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[Wards]', N'U') IS NULL
            BEGIN
                CREATE TABLE [Wards] (
                    [Code] nvarchar(20) NOT NULL CONSTRAINT [PK_Wards] PRIMARY KEY,
                    [ProvinceCode] nvarchar(10) NOT NULL,
                    [Name] nvarchar(150) NOT NULL,
                    [FullName] nvarchar(180) NOT NULL,
                    CONSTRAINT [FK_Wards_Provinces_ProvinceCode] FOREIGN KEY ([ProvinceCode]) REFERENCES [Provinces]([Code])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'Provinces', N'Type') IS NOT NULL
                ALTER TABLE [Provinces] DROP COLUMN [Type];
            IF COL_LENGTH(N'Provinces', N'TaxCode') IS NOT NULL
                ALTER TABLE [Provinces] DROP COLUMN [TaxCode];
            IF COL_LENGTH(N'Wards', N'Type') IS NOT NULL
                ALTER TABLE [Wards] DROP COLUMN [Type];
            IF COL_LENGTH(N'Wards', N'OldDistrictTaxCode') IS NOT NULL
                ALTER TABLE [Wards] DROP COLUMN [OldDistrictTaxCode];
            IF COL_LENGTH(N'Wards', N'OldDistrictName') IS NOT NULL
                ALTER TABLE [Wards] DROP COLUMN [OldDistrictName];
            """);
        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_Wards_ProvinceCode'
                  AND [object_id] = OBJECT_ID(N'[Wards]')
            )
                CREATE INDEX [IX_Wards_ProvinceCode] ON [Wards] ([ProvinceCode]);
            """);

        SeedAdministrativeUnits(app, dbContext);
    }

    private static void SeedAdministrativeUnits(WebApplication app, ApplicationDbContext dbContext)
    {
        var provinceCount = dbContext.Provinces.Count();
        var wardCount = dbContext.Wards.Count();
        var hasExpectedSeedCodes = dbContext.Provinces.Any(province => province.Code == ExpectedFirstProvinceCode)
            && dbContext.Wards.Any(ward => ward.Code == ExpectedFirstWardCode);
        if (provinceCount == ExpectedProvinceCount && wardCount == ExpectedWardCount && hasExpectedSeedCodes)
        {
            return;
        }

        var seedPath = Path.GetFullPath(Path.Combine(
            app.Environment.ContentRootPath,
            "..",
            "database",
            "seeds",
            "seed_vietnam_administrative_units_2025.sql"));
        if (!File.Exists(seedPath))
        {
            return;
        }

        var seedSql = File.ReadAllText(seedPath);
        dbContext.Database.ExecuteSqlRaw(seedSql);
    }

    private static void EnsureAdminUserSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'USER', N'isLocked') IS NULL
                ALTER TABLE [USER] ADD [isLocked] bit NOT NULL CONSTRAINT [DF_USER_isLocked] DEFAULT (0);
            """);
    }

    private static void EnsurePasswordResetSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[PASSWORD_RESET_TOKEN]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PASSWORD_RESET_TOKEN] (
                    [resetTokenId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PASSWORD_RESET_TOKEN] PRIMARY KEY,
                    [userId] int NOT NULL,
                    [tokenHash] nvarchar(128) NOT NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_PASSWORD_RESET_TOKEN_createdAt] DEFAULT (getutcdate()),
                    [expiresAt] datetime NOT NULL,
                    [usedAt] datetime NULL,
                    CONSTRAINT [FK_PASSWORD_RESET_TOKEN_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_PASSWORD_RESET_TOKEN_userId'
                    AND object_id = OBJECT_ID(N'[PASSWORD_RESET_TOKEN]')
            )
            BEGIN
                CREATE INDEX [IX_PASSWORD_RESET_TOKEN_userId] ON [PASSWORD_RESET_TOKEN] ([userId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_PASSWORD_RESET_TOKEN_tokenHash'
                    AND object_id = OBJECT_ID(N'[PASSWORD_RESET_TOKEN]')
            )
            BEGIN
                CREATE INDEX [IX_PASSWORD_RESET_TOKEN_tokenHash] ON [PASSWORD_RESET_TOKEN] ([tokenHash]);
            END
            """);
    }

    private static void EnsurePlayerProfileSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'playFrequency') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [playFrequency] nvarchar(50) NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'preferredTimeSlot') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [preferredTimeSlot] nvarchar(50) NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'bio') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [bio] nvarchar(500) NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'birthDate') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [birthDate] date NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'gender') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [gender] nvarchar(30) NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'heightCm') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [heightCm] float NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PLAYER', N'weightKg') IS NULL
            BEGIN
                ALTER TABLE [PLAYER] ADD [weightKg] float NULL;
            END
            """);
    }

    private static void EnsureCommunitySchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[SOCIAL_GROUP]', N'U') IS NULL
            BEGIN
                CREATE TABLE [SOCIAL_GROUP] (
                    [groupId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SOCIAL_GROUP] PRIMARY KEY,
                    [ownerId] int NOT NULL,
                    [groupName] nvarchar(200) NOT NULL,
                    [description] nvarchar(max) NULL,
                    [groupType] nvarchar(50) NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_groupType] DEFAULT (N'Public'),
                    [coverImageUrl] nvarchar(500) NULL,
                    [rules] nvarchar(max) NULL,
                    [activeLocation] nvarchar(255) NULL,
                    [overallRating] float NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_overallRating] DEFAULT (0),
                    [ratingCount] int NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_ratingCount] DEFAULT (0),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_createdAt] DEFAULT (getdate()),
                    CONSTRAINT [FK_SOCIAL_GROUP_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [PLAYER]([playerId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'SOCIAL_GROUP', N'rules') IS NULL
                ALTER TABLE [SOCIAL_GROUP] ADD [rules] nvarchar(max) NULL;
            IF COL_LENGTH(N'SOCIAL_GROUP', N'activeLocation') IS NULL
                ALTER TABLE [SOCIAL_GROUP] ADD [activeLocation] nvarchar(255) NULL;
            IF COL_LENGTH(N'SOCIAL_GROUP', N'overallRating') IS NULL
                ALTER TABLE [SOCIAL_GROUP] ADD [overallRating] float NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_overallRating] DEFAULT (0);
            IF COL_LENGTH(N'SOCIAL_GROUP', N'ratingCount') IS NULL
                ALTER TABLE [SOCIAL_GROUP] ADD [ratingCount] int NOT NULL CONSTRAINT [DF_SOCIAL_GROUP_ratingCount] DEFAULT (0);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[GROUP_IMAGE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [GROUP_IMAGE] (
                    [groupImageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_GROUP_IMAGE] PRIMARY KEY,
                    [groupId] int NOT NULL,
                    [imageUrl] nvarchar(1000) NOT NULL,
                    [caption] nvarchar(200) NULL,
                    [sortOrder] int NOT NULL CONSTRAINT [DF_GROUP_IMAGE_sortOrder] DEFAULT (0),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_GROUP_IMAGE_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_GROUP_IMAGE_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]) ON DELETE CASCADE
                );

                CREATE INDEX [IX_GROUP_IMAGE_groupId] ON [GROUP_IMAGE] ([groupId], [sortOrder]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[GROUP_MEMBER]', N'U') IS NULL
            BEGIN
                CREATE TABLE [GROUP_MEMBER] (
                    [groupId] int NOT NULL,
                    [userId] int NOT NULL,
                    [role] nvarchar(50) NOT NULL CONSTRAINT [DF_GROUP_MEMBER_role] DEFAULT (N'Member'),
                    [status] nvarchar(50) NOT NULL CONSTRAINT [DF_GROUP_MEMBER_status] DEFAULT (N'Accepted'),
                    [joinedAt] datetime NOT NULL CONSTRAINT [DF_GROUP_MEMBER_joinedAt] DEFAULT (getdate()),
                    CONSTRAINT [PK_GROUP_MEMBER] PRIMARY KEY ([groupId], [userId]),
                    CONSTRAINT [FK_GROUP_MEMBER_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]),
                    CONSTRAINT [FK_GROUP_MEMBER_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[POST]', N'U') IS NULL
            BEGIN
                CREATE TABLE [POST] (
                    [postId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST] PRIMARY KEY,
                    [authorId] int NOT NULL,
                    [groupId] int NULL,
                    [content] nvarchar(max) NULL,
                    [postType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_postType] DEFAULT (N'Post'),
                    [visibility] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_visibility] DEFAULT (N'Public'),
                    [expiresAt] datetime NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_createdAt] DEFAULT (getdate()),
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_POST_updatedAt] DEFAULT (getdate()),
                    CONSTRAINT [FK_POST_AUTHOR] FOREIGN KEY ([authorId]) REFERENCES [USER]([userId]),
                    CONSTRAINT [FK_POST_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[POST_COMMENT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [POST_COMMENT] (
                    [commentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_COMMENT] PRIMARY KEY,
                    [postId] int NOT NULL,
                    [userId] int NOT NULL,
                    [parentCommentId] int NULL,
                    [content] nvarchar(max) NOT NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_COMMENT_createdAt] DEFAULT (getdate()),
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_POST_COMMENT_updatedAt] DEFAULT (getdate()),
                    CONSTRAINT [FK_POST_COMMENT_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId]),
                    CONSTRAINT [FK_POST_COMMENT_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId]),
                    CONSTRAINT [FK_POST_COMMENT_PARENT] FOREIGN KEY ([parentCommentId]) REFERENCES [POST_COMMENT]([commentId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[POST_LIKE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [POST_LIKE] (
                    [likeId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_LIKE] PRIMARY KEY,
                    [postId] int NOT NULL,
                    [userId] int NOT NULL,
                    [reactionType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_LIKE_reactionType] DEFAULT (N'Like'),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_POST_LIKE_createdAt] DEFAULT (getdate()),
                    CONSTRAINT [FK_POST_LIKE_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId]),
                    CONSTRAINT [FK_POST_LIKE_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[POST_MEDIA]', N'U') IS NULL
            BEGIN
                CREATE TABLE [POST_MEDIA] (
                    [mediaId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POST_MEDIA] PRIMARY KEY,
                    [postId] int NOT NULL,
                    [mediaUrl] nvarchar(500) NOT NULL,
                    [mediaType] nvarchar(50) NOT NULL CONSTRAINT [DF_POST_MEDIA_mediaType] DEFAULT (N'Image'),
                    [displayOrder] int NOT NULL CONSTRAINT [DF_POST_MEDIA_displayOrder] DEFAULT (0),
                    CONSTRAINT [FK_POST_MEDIA_POST] FOREIGN KEY ([postId]) REFERENCES [POST]([postId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[CONVERSATION]', N'U') IS NULL
            BEGIN
                CREATE TABLE [CONVERSATION] (
                    [conversationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CONVERSATION] PRIMARY KEY,
                    [groupId] int NULL,
                    [matchId] int NULL,
                    [conversationType] nvarchar(50) NOT NULL CONSTRAINT [DF_CONVERSATION_conversationType] DEFAULT (N'Direct'),
                    [conversationName] nvarchar(200) NULL,
                    [lastMessageAt] datetime NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_CONVERSATION_createdAt] DEFAULT (getdate()),
                    CONSTRAINT [FK_CONVERSATION_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[CONVERSATION_PARTICIPANT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [CONVERSATION_PARTICIPANT] (
                    [conversationId] int NOT NULL,
                    [userId] int NOT NULL,
                    [joinedAt] datetime NOT NULL CONSTRAINT [DF_CONV_PARTICIPANT_joinedAt] DEFAULT (getdate()),
                    [lastReadAt] datetime NULL,
                    CONSTRAINT [PK_CONVERSATION_PARTICIPANT] PRIMARY KEY ([conversationId], [userId]),
                    CONSTRAINT [FK_CONV_PARTICIPANT_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_CONV_PARTICIPANT_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[MESSAGE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MESSAGE] (
                    [messageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MESSAGE] PRIMARY KEY,
                    [conversationId] int NOT NULL,
                    [senderId] int NOT NULL,
                    [content] nvarchar(max) NULL,
                    [messageType] nvarchar(50) NOT NULL CONSTRAINT [DF_MESSAGE_messageType] DEFAULT (N'Text'),
                    [mediaUrl] nvarchar(500) NULL,
                    [replyToMessageId] int NULL,
                    [sentAt] datetime NOT NULL CONSTRAINT [DF_MESSAGE_sentAt] DEFAULT (getdate()),
                    [isDeleted] bit NOT NULL CONSTRAINT [DF_MESSAGE_isDeleted] DEFAULT (0),
                    [isPinned] bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0),
                    CONSTRAINT [FK_MESSAGE_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MESSAGE_SENDER] FOREIGN KEY ([senderId]) REFERENCES [USER]([userId]),
                    CONSTRAINT [FK_MESSAGE_REPLY] FOREIGN KEY ([replyToMessageId]) REFERENCES [MESSAGE]([messageId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'MESSAGE', N'isPinned') IS NULL
                ALTER TABLE [MESSAGE] ADD [isPinned] bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[NOTIFICATION_LOG]', N'U') IS NULL
            BEGIN
                CREATE TABLE [NOTIFICATION_LOG] (
                    [notifId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_NOTIFICATION_LOG] PRIMARY KEY,
                    [userId] int NOT NULL,
                    [notificationType] nvarchar(30) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_notificationType] DEFAULT (N'system'),
                    [title] nvarchar(200) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_title] DEFAULT (N'Thông báo'),
                    [message] nvarchar(max) NOT NULL,
                    [tone] nvarchar(20) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_tone] DEFAULT (N'default'),
                    [linkTo] nvarchar(500) NULL,
                    [linkLabel] nvarchar(100) NULL,
                    [createdAt] datetime2 NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_createdAt] DEFAULT (getutcdate()),
                    [isRead] bit NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_isRead] DEFAULT (0),
                    CONSTRAINT [FK_NOTIFICATION_LOG_USER] FOREIGN KEY ([userId]) REFERENCES [USER]([userId])
                );
            END
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'notificationType') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [notificationType] nvarchar(30) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_notificationType] DEFAULT (N'system');
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'title') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [title] nvarchar(200) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_title] DEFAULT (N'Thông báo');
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'tone') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [tone] nvarchar(20) NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_tone] DEFAULT (N'default');
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'linkTo') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [linkTo] nvarchar(500) NULL;
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'linkLabel') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [linkLabel] nvarchar(100) NULL;
            IF COL_LENGTH(N'NOTIFICATION_LOG', N'createdAt') IS NULL
                ALTER TABLE [NOTIFICATION_LOG] ADD [createdAt] datetime2 NOT NULL CONSTRAINT [DF_NOTIFICATION_LOG_createdAt] DEFAULT (getutcdate());
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_NOTIFICATION_LOG_user_unread_created'
                  AND [object_id] = OBJECT_ID(N'[NOTIFICATION_LOG]')
            )
                CREATE INDEX [IX_NOTIFICATION_LOG_user_unread_created]
                ON [NOTIFICATION_LOG] ([userId], [isRead], [createdAt] DESC);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'POST', N'groupId') IS NULL
            BEGIN
                ALTER TABLE [POST] ADD [groupId] int NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'CONVERSATION', N'groupId') IS NULL
            BEGIN
                ALTER TABLE [CONVERSATION] ADD [groupId] int NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'CONVERSATION', N'matchId') IS NULL
            BEGIN
                ALTER TABLE [CONVERSATION] ADD [matchId] int NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_POST_SOCIAL_GROUP'
            )
            BEGIN
                ALTER TABLE [POST]
                ADD CONSTRAINT [FK_POST_SOCIAL_GROUP]
                FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CONVERSATION_SOCIAL_GROUP'
            )
            BEGIN
                ALTER TABLE [CONVERSATION]
                ADD CONSTRAINT [FK_CONVERSATION_SOCIAL_GROUP]
                FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP]([groupId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[MATCH]', N'U') IS NOT NULL
                AND COL_LENGTH(N'CONVERSATION', N'matchId') IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CONVERSATION_MATCH'
                )
            BEGIN
                ALTER TABLE [CONVERSATION]
                ADD CONSTRAINT [FK_CONVERSATION_MATCH]
                FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_GROUP_MEMBER_userId'
                    AND object_id = OBJECT_ID(N'[GROUP_MEMBER]')
            )
            BEGIN
                CREATE INDEX [IX_GROUP_MEMBER_userId] ON [GROUP_MEMBER] ([userId], [status]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_SOCIAL_GROUP_ownerId'
                    AND object_id = OBJECT_ID(N'[SOCIAL_GROUP]')
            )
            BEGIN
                CREATE INDEX [IX_SOCIAL_GROUP_ownerId] ON [SOCIAL_GROUP] ([ownerId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_POST_groupId'
                    AND object_id = OBJECT_ID(N'[POST]')
            )
            BEGIN
                CREATE INDEX [IX_POST_groupId] ON [POST] ([groupId]) WHERE [groupId] IS NOT NULL;
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_CONVERSATION_groupId'
                    AND object_id = OBJECT_ID(N'[CONVERSATION]')
            )
            BEGIN
                CREATE INDEX [IX_CONVERSATION_groupId] ON [CONVERSATION] ([groupId]) WHERE [groupId] IS NOT NULL;
            END
            """);
    }

    private static void EnsureUserProfileSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'USER', N'commune') IS NULL
            BEGIN
                ALTER TABLE [USER] ADD [commune] nvarchar(150) NULL;
            END
            """);
    }

    private static void EnsureCommunityReportSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[COMMUNITY_REPORT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [COMMUNITY_REPORT] (
                    [communityReportId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_COMMUNITY_REPORT] PRIMARY KEY,
                    [reporterUserId] int NOT NULL,
                    [targetType] nvarchar(50) NOT NULL,
                    [targetId] int NULL,
                    [targetLabel] nvarchar(250) NOT NULL,
                    [reason] nvarchar(200) NOT NULL,
                    [description] nvarchar(2000) NULL,
                    [status] nvarchar(30) NOT NULL CONSTRAINT [DF_COMMUNITY_REPORT_status] DEFAULT (N'Open'),
                    [priority] nvarchar(30) NOT NULL CONSTRAINT [DF_COMMUNITY_REPORT_priority] DEFAULT (N'Normal'),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_COMMUNITY_REPORT_createdAt] DEFAULT (getutcdate()),
                    [reviewedAt] datetime NULL,
                    [reviewedByUserId] int NULL,
                    [resolutionNote] nvarchar(1000) NULL,
                    CONSTRAINT [FK_COMMUNITY_REPORT_REPORTER] FOREIGN KEY ([reporterUserId]) REFERENCES [USER]([userId]),
                    CONSTRAINT [FK_COMMUNITY_REPORT_REVIEWER] FOREIGN KEY ([reviewedByUserId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_COMMUNITY_REPORT_reporterUserId' AND object_id = OBJECT_ID(N'[COMMUNITY_REPORT]'))
                CREATE INDEX [IX_COMMUNITY_REPORT_reporterUserId] ON [COMMUNITY_REPORT] ([reporterUserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_COMMUNITY_REPORT_reviewedByUserId' AND object_id = OBJECT_ID(N'[COMMUNITY_REPORT]'))
                CREATE INDEX [IX_COMMUNITY_REPORT_reviewedByUserId] ON [COMMUNITY_REPORT] ([reviewedByUserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_COMMUNITY_REPORT_status' AND object_id = OBJECT_ID(N'[COMMUNITY_REPORT]'))
                CREATE INDEX [IX_COMMUNITY_REPORT_status] ON [COMMUNITY_REPORT] ([status]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_COMMUNITY_REPORT_targetType' AND object_id = OBJECT_ID(N'[COMMUNITY_REPORT]'))
                CREATE INDEX [IX_COMMUNITY_REPORT_targetType] ON [COMMUNITY_REPORT] ([targetType]);
            """);
    }

    private static void EnsureAdminReviewSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'RATING_HISTORY', N'isHidden') IS NULL
                ALTER TABLE [RATING_HISTORY] ADD [isHidden] bit NOT NULL CONSTRAINT [DF_RATING_HISTORY_isHidden] DEFAULT (0);
            IF COL_LENGTH(N'RATING_HISTORY', N'moderationStatus') IS NULL
                ALTER TABLE [RATING_HISTORY] ADD [moderationStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_RATING_HISTORY_moderationStatus] DEFAULT (N'Visible');
            IF COL_LENGTH(N'RATING_HISTORY', N'moderationNote') IS NULL
                ALTER TABLE [RATING_HISTORY] ADD [moderationNote] nvarchar(1000) NULL;
            IF COL_LENGTH(N'RATING_HISTORY', N'moderatedAt') IS NULL
                ALTER TABLE [RATING_HISTORY] ADD [moderatedAt] datetime NULL;
            IF COL_LENGTH(N'RATING_HISTORY', N'moderatedByUserId') IS NULL
                ALTER TABLE [RATING_HISTORY] ADD [moderatedByUserId] int NULL;
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RATING_HISTORY_MODERATOR')
                ALTER TABLE [RATING_HISTORY] ADD CONSTRAINT [FK_RATING_HISTORY_MODERATOR]
                FOREIGN KEY ([moderatedByUserId]) REFERENCES [USER]([userId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RATING_HISTORY_moderatedByUserId' AND object_id = OBJECT_ID(N'[RATING_HISTORY]'))
                CREATE INDEX [IX_RATING_HISTORY_moderatedByUserId] ON [RATING_HISTORY] ([moderatedByUserId]);
            """);
    }

    private static void EnsureAdminSettingsSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[PLATFORM_SETTING]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PLATFORM_SETTING] (
                    [platformSettingId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PLATFORM_SETTING] PRIMARY KEY,
                    [settingKey] nvarchar(100) NOT NULL,
                    [settingValue] nvarchar(500) NOT NULL,
                    [settingGroup] nvarchar(100) NOT NULL CONSTRAINT [DF_PLATFORM_SETTING_settingGroup] DEFAULT (N'General'),
                    [description] nvarchar(500) NOT NULL CONSTRAINT [DF_PLATFORM_SETTING_description] DEFAULT (N''),
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_PLATFORM_SETTING_updatedAt] DEFAULT (getutcdate()),
                    [updatedByUserId] int NULL,
                    CONSTRAINT [FK_PLATFORM_SETTING_UPDATED_BY] FOREIGN KEY ([updatedByUserId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_PLATFORM_SETTING_settingKey' AND object_id = OBJECT_ID(N'[PLATFORM_SETTING]'))
                CREATE UNIQUE INDEX [UQ_PLATFORM_SETTING_settingKey] ON [PLATFORM_SETTING] ([settingKey]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PLATFORM_SETTING_updatedByUserId' AND object_id = OBJECT_ID(N'[PLATFORM_SETTING]'))
                CREATE INDEX [IX_PLATFORM_SETTING_updatedByUserId] ON [PLATFORM_SETTING] ([updatedByUserId]);
            """);
    }

    private static void EnsureOwnerVenueSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'VENUE', N'isOpen') IS NULL
                ALTER TABLE [VENUE] ADD [isOpen] bit NOT NULL CONSTRAINT [DF_VENUE_isOpen] DEFAULT (1);
            IF COL_LENGTH(N'VENUE', N'approvalStatus') IS NULL
                ALTER TABLE [VENUE] ADD [approvalStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_VENUE_approvalStatus] DEFAULT (N'Draft');
            IF COL_LENGTH(N'VENUE', N'rejectionReason') IS NULL
                ALTER TABLE [VENUE] ADD [rejectionReason] nvarchar(500) NULL;
            IF COL_LENGTH(N'COURT', N'courtType') IS NULL
                ALTER TABLE [COURT] ADD [courtType] nvarchar(100) NOT NULL CONSTRAINT [DF_COURT_courtType] DEFAULT (N'Standard');
            IF COL_LENGTH(N'COURT', N'hourlyPrice') IS NULL
                ALTER TABLE [COURT] ADD [hourlyPrice] decimal(18,2) NOT NULL CONSTRAINT [DF_COURT_hourlyPrice] DEFAULT (0);
            IF COL_LENGTH(N'BOOKING', N'ownerEntryType') IS NULL
                ALTER TABLE [BOOKING] ADD [ownerEntryType] nvarchar(30) NULL;
            IF COL_LENGTH(N'BOOKING', N'title') IS NULL
                ALTER TABLE [BOOKING] ADD [title] nvarchar(200) NULL;
            IF COL_LENGTH(N'BOOKING', N'bookingCode') IS NULL
                ALTER TABLE [BOOKING] ADD [bookingCode] nvarchar(30) NULL;
            IF COL_LENGTH(N'BOOKING', N'createdAt') IS NULL
                ALTER TABLE [BOOKING] ADD [createdAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_createdAt] DEFAULT (getutcdate());
            IF COL_LENGTH(N'BOOKING', N'holdExpiresAt') IS NULL
                ALTER TABLE [BOOKING] ADD [holdExpiresAt] datetime NULL;
            IF COL_LENGTH(N'BOOKING', N'hourlyPriceSnapshot') IS NULL
                ALTER TABLE [BOOKING] ADD [hourlyPriceSnapshot] decimal(18,2) NOT NULL CONSTRAINT [DF_BOOKING_hourlyPriceSnapshot] DEFAULT (0);
            IF COL_LENGTH(N'BOOKING', N'courtAmount') IS NULL
                ALTER TABLE [BOOKING] ADD [courtAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_BOOKING_courtAmount] DEFAULT (0);
            IF COL_LENGTH(N'BOOKING', N'totalAmount') IS NULL
                ALTER TABLE [BOOKING] ADD [totalAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_BOOKING_totalAmount] DEFAULT (0);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[VENUE_IMAGE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [VENUE_IMAGE] (
                    [venueImageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_VENUE_IMAGE] PRIMARY KEY,
                    [venueId] int NOT NULL,
                    [imageUrl] nvarchar(1000) NOT NULL,
                    [caption] nvarchar(200) NULL,
                    [isPrimary] bit NOT NULL CONSTRAINT [DF_VENUE_IMAGE_isPrimary] DEFAULT (0),
                    [sortOrder] int NOT NULL CONSTRAINT [DF_VENUE_IMAGE_sortOrder] DEFAULT (0),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_VENUE_IMAGE_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_VENUE_IMAGE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_VENUE_IMAGE_venueId] ON [VENUE_IMAGE] ([venueId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[BOOKING_STATUS_HISTORY]', N'U') IS NULL
            BEGIN
                CREATE TABLE [BOOKING_STATUS_HISTORY] (
                    [bookingStatusHistoryId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_STATUS_HISTORY] PRIMARY KEY,
                    [bookingId] int NOT NULL,
                    [fromStatus] nvarchar(50) NULL,
                    [toStatus] nvarchar(50) NOT NULL,
                    [reason] nvarchar(500) NULL,
                    [actorUserId] int NULL,
                    [changedAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_STATUS_HISTORY_changedAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_BOOKING_STATUS_HISTORY_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_BOOKING_STATUS_HISTORY_bookingId] ON [BOOKING_STATUS_HISTORY] ([bookingId]);
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_court_time' AND object_id = OBJECT_ID(N'[BOOKING]'))
                CREATE INDEX [IX_BOOKING_court_time] ON [BOOKING] ([courtId], [startTime], [endTime], [status]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_BOOKING_bookingCode' AND object_id = OBJECT_ID(N'[BOOKING]'))
                CREATE UNIQUE INDEX [UQ_BOOKING_bookingCode] ON [BOOKING] ([bookingCode]) WHERE [bookingCode] IS NOT NULL;
            """);
    }

    private static void EnsureListingFeeSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[LISTING_FEE_SETTING]', N'U') IS NULL
            BEGIN
                CREATE TABLE [LISTING_FEE_SETTING] (
                    [listingFeeSettingId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LISTING_FEE_SETTING] PRIMARY KEY,
                    [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_LISTING_FEE_SETTING_updatedAt] DEFAULT (getutcdate()),
                    [updatedByUserId] int NULL,
                    CONSTRAINT [FK_LISTING_FEE_SETTING_USER] FOREIGN KEY ([updatedByUserId]) REFERENCES [USER]([userId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [VENUE_LISTING_PAYMENT] (
                    [venueListingPaymentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_VENUE_LISTING_PAYMENT] PRIMARY KEY,
                    [venueId] int NOT NULL,
                    [months] int NOT NULL,
                    [activeCourtCount] int NOT NULL,
                    [pricePerCourtPerMonth] decimal(18,2) NOT NULL,
                    [amount] decimal(18,2) NOT NULL,
                    [status] nvarchar(30) NOT NULL,
                    [receiptImageUrl] nvarchar(1000) NULL,
                    [rejectionReason] nvarchar(500) NULL,
                    [submittedAt] datetime NOT NULL CONSTRAINT [DF_VENUE_LISTING_PAYMENT_submittedAt] DEFAULT (getutcdate()),
                    [reviewedAt] datetime NULL,
                    [reviewedByUserId] int NULL,
                    [paidFrom] datetime NULL,
                    [paidUntil] datetime NULL,
                    CONSTRAINT [FK_VENUE_LISTING_PAYMENT_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_VENUE_LISTING_PAYMENT_REVIEWER] FOREIGN KEY ([reviewedByUserId]) REFERENCES [USER]([userId])
                );
                CREATE INDEX [IX_VENUE_LISTING_PAYMENT_venueId] ON [VENUE_LISTING_PAYMENT] ([venueId]);
                CREATE INDEX [IX_VENUE_LISTING_PAYMENT_status] ON [VENUE_LISTING_PAYMENT] ([status]);
            END
            """);
    }

    private static void EnsurePaymentSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[OWNER_BANK_ACCOUNT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [OWNER_BANK_ACCOUNT] (
                    [ownerBankAccountId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OWNER_BANK_ACCOUNT] PRIMARY KEY,
                    [ownerId] int NOT NULL,
                    [bankCode] nvarchar(30) NOT NULL,
                    [bankName] nvarchar(150) NOT NULL,
                    [accountNumber] nvarchar(50) NOT NULL,
                    [accountHolderName] nvarchar(200) NOT NULL,
                    [isActive] bit NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_isActive] DEFAULT (1),
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_createdAt] DEFAULT (getutcdate()),
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_OWNER_BANK_ACCOUNT_updatedAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_OWNER_BANK_ACCOUNT_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [VENUE_OWNER]([ownerId]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [UQ_OWNER_BANK_ACCOUNT_ownerId] ON [OWNER_BANK_ACCOUNT] ([ownerId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'PAYMENT', N'transferCode') IS NULL ALTER TABLE [PAYMENT] ADD [transferCode] nvarchar(40) NULL;
            IF COL_LENGTH(N'PAYMENT', N'transferContent') IS NULL ALTER TABLE [PAYMENT] ADD [transferContent] nvarchar(140) NULL;
            IF COL_LENGTH(N'PAYMENT', N'bankCode') IS NULL ALTER TABLE [PAYMENT] ADD [bankCode] nvarchar(30) NULL;
            IF COL_LENGTH(N'PAYMENT', N'bankName') IS NULL ALTER TABLE [PAYMENT] ADD [bankName] nvarchar(150) NULL;
            IF COL_LENGTH(N'PAYMENT', N'bankAccountNumber') IS NULL ALTER TABLE [PAYMENT] ADD [bankAccountNumber] nvarchar(50) NULL;
            IF COL_LENGTH(N'PAYMENT', N'bankAccountName') IS NULL ALTER TABLE [PAYMENT] ADD [bankAccountName] nvarchar(200) NULL;
            IF COL_LENGTH(N'PAYMENT', N'qrImageUrl') IS NULL ALTER TABLE [PAYMENT] ADD [qrImageUrl] nvarchar(2000) NULL;
            IF COL_LENGTH(N'PAYMENT', N'receiptImageUrl') IS NULL ALTER TABLE [PAYMENT] ADD [receiptImageUrl] nvarchar(1000) NULL;
            IF COL_LENGTH(N'PAYMENT', N'submittedAt') IS NULL ALTER TABLE [PAYMENT] ADD [submittedAt] datetime NULL;
            IF COL_LENGTH(N'PAYMENT', N'verifiedAt') IS NULL ALTER TABLE [PAYMENT] ADD [verifiedAt] datetime NULL;
            IF COL_LENGTH(N'PAYMENT', N'verifiedByUserId') IS NULL ALTER TABLE [PAYMENT] ADD [verifiedByUserId] int NULL;
            IF COL_LENGTH(N'PAYMENT', N'rejectionReason') IS NULL ALTER TABLE [PAYMENT] ADD [rejectionReason] nvarchar(500) NULL;
            """);

        // CREATE INDEX must be compiled after transferCode has been added. Dynamic SQL
        // prevents SQL Server from resolving the new column while compiling the ALTER batch.

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_PAYMENT_transferCode' AND object_id = OBJECT_ID(N'[PAYMENT]'))
                EXEC(N'CREATE UNIQUE INDEX [UQ_PAYMENT_transferCode] ON [PAYMENT] ([transferCode]) WHERE [transferCode] IS NOT NULL;');
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[PAYMENT_STATUS_HISTORY]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PAYMENT_STATUS_HISTORY] (
                    [paymentStatusHistoryId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PAYMENT_STATUS_HISTORY] PRIMARY KEY,
                    [paymentId] int NOT NULL,
                    [fromStatus] nvarchar(50) NULL,
                    [toStatus] nvarchar(50) NOT NULL,
                    [action] nvarchar(100) NOT NULL,
                    [reason] nvarchar(500) NULL,
                    [actorUserId] int NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_PAYMENT_STATUS_HISTORY_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_PAYMENT_STATUS_HISTORY_PAYMENT] FOREIGN KEY ([paymentId]) REFERENCES [PAYMENT]([paymentId]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_PAYMENT_STATUS_HISTORY_paymentId] ON [PAYMENT_STATUS_HISTORY] ([paymentId]);
            END
            """);
    }

    private static void EnsureStaffOperationSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'STAFF', N'permissions') IS NULL
                ALTER TABLE [STAFF] ADD [permissions] nvarchar(500) NOT NULL CONSTRAINT [DF_STAFF_permissions] DEFAULT (N'ViewBookings,VerifyBooking,ConfirmPayment,CheckIn,MarkNoShow');
            IF COL_LENGTH(N'STAFF', N'isActive') IS NULL
                ALTER TABLE [STAFF] ADD [isActive] bit NOT NULL CONSTRAINT [DF_STAFF_isActive] DEFAULT (1);
            IF COL_LENGTH(N'STAFF', N'assignedAt') IS NULL
                ALTER TABLE [STAFF] ADD [assignedAt] datetime NOT NULL CONSTRAINT [DF_STAFF_assignedAt] DEFAULT (getutcdate());
            IF COL_LENGTH(N'STAFF', N'assignedByUserId') IS NULL
                ALTER TABLE [STAFF] ADD [assignedByUserId] int NULL;
            IF COL_LENGTH(N'STAFF', N'revokedAt') IS NULL
                ALTER TABLE [STAFF] ADD [revokedAt] datetime NULL;
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_STAFF_userId_venueId' AND object_id = OBJECT_ID(N'[STAFF]'))
                CREATE UNIQUE INDEX [UQ_STAFF_userId_venueId] ON [STAFF] ([userId], [venueId]);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[BOOKING_OPERATION]', N'U') IS NULL
            BEGIN
                CREATE TABLE [BOOKING_OPERATION] (
                    [bookingOperationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_OPERATION] PRIMARY KEY,
                    [bookingId] int NOT NULL,
                    [checkInStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_BOOKING_OPERATION_checkInStatus] DEFAULT (N'Ready'),
                    [codeVerifiedAt] datetime NULL,
                    [codeVerifiedByUserId] int NULL,
                    [paymentConfirmedAt] datetime NULL,
                    [paymentConfirmedByUserId] int NULL,
                    [checkedInAt] datetime NULL,
                    [checkedInByUserId] int NULL,
                    [noShowAt] datetime NULL,
                    [noShowByUserId] int NULL,
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_OPERATION_updatedAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_BOOKING_OPERATION_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [UQ_BOOKING_OPERATION_bookingId] ON [BOOKING_OPERATION] ([bookingId]);
            END
            """);
    }

    private static void EnsureBookingSlotSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[BOOKING_CHECKIN_GROUP]', N'U') IS NULL
            BEGIN
                CREATE TABLE [BOOKING_CHECKIN_GROUP] (
                    [bookingCheckInGroupId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_CHECKIN_GROUP] PRIMARY KEY,
                    [bookingId] int NOT NULL,
                    [courtId] int NOT NULL,
                    [startTime] datetime NOT NULL,
                    [endTime] datetime NOT NULL,
                    [checkInCode] nvarchar(30) NOT NULL,
                    [checkInStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_BOOKING_CHECKIN_GROUP_checkInStatus] DEFAULT (N'Ready'),
                    [codeVerifiedAt] datetime NULL,
                    [codeVerifiedByUserId] int NULL,
                    [checkedInAt] datetime NULL,
                    [checkedInByUserId] int NULL,
                    [noShowAt] datetime NULL,
                    [noShowByUserId] int NULL,
                    [updatedAt] datetime NOT NULL CONSTRAINT [DF_BOOKING_CHECKIN_GROUP_updatedAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_BOOKING_CHECKIN_GROUP_BOOKING_bookingId] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_BOOKING_CHECKIN_GROUP_COURT_courtId] FOREIGN KEY ([courtId]) REFERENCES [COURT]([courtId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[BOOKING_SLOT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [BOOKING_SLOT] (
                    [bookingSlotId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BOOKING_SLOT] PRIMARY KEY,
                    [bookingId] int NOT NULL,
                    [courtId] int NOT NULL,
                    [checkInGroupId] int NULL,
                    [startTime] datetime NOT NULL,
                    [endTime] datetime NOT NULL,
                    [hourlyPriceSnapshot] decimal(18,2) NOT NULL,
                    [courtAmount] decimal(18,2) NOT NULL,
                    CONSTRAINT [FK_BOOKING_SLOT_BOOKING_bookingId] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_BOOKING_SLOT_COURT_courtId] FOREIGN KEY ([courtId]) REFERENCES [COURT]([courtId]),
                    CONSTRAINT [FK_BOOKING_SLOT_BOOKING_CHECKIN_GROUP_checkInGroupId] FOREIGN KEY ([checkInGroupId]) REFERENCES [BOOKING_CHECKIN_GROUP]([bookingCheckInGroupId])
                );
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_CHECKIN_GROUP_booking_time' AND object_id = OBJECT_ID(N'[BOOKING_CHECKIN_GROUP]'))
                CREATE INDEX [IX_BOOKING_CHECKIN_GROUP_booking_time] ON [BOOKING_CHECKIN_GROUP] ([bookingId], [startTime]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_CHECKIN_GROUP_courtId' AND object_id = OBJECT_ID(N'[BOOKING_CHECKIN_GROUP]'))
                CREATE INDEX [IX_BOOKING_CHECKIN_GROUP_courtId] ON [BOOKING_CHECKIN_GROUP] ([courtId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_BOOKING_CHECKIN_GROUP_code' AND object_id = OBJECT_ID(N'[BOOKING_CHECKIN_GROUP]'))
                CREATE UNIQUE INDEX [UQ_BOOKING_CHECKIN_GROUP_code] ON [BOOKING_CHECKIN_GROUP] ([checkInCode]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_SLOT_booking_time' AND object_id = OBJECT_ID(N'[BOOKING_SLOT]'))
                CREATE INDEX [IX_BOOKING_SLOT_booking_time] ON [BOOKING_SLOT] ([bookingId], [startTime]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_SLOT_checkInGroupId' AND object_id = OBJECT_ID(N'[BOOKING_SLOT]'))
                CREATE INDEX [IX_BOOKING_SLOT_checkInGroupId] ON [BOOKING_SLOT] ([checkInGroupId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BOOKING_SLOT_court_time' AND object_id = OBJECT_ID(N'[BOOKING_SLOT]'))
                CREATE INDEX [IX_BOOKING_SLOT_court_time] ON [BOOKING_SLOT] ([courtId], [startTime], [endTime]);
            """);
    }
    private static void EnsurePlayerPhase7Schema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[FAVORITE_VENUE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FAVORITE_VENUE] (
                    [playerId] int NOT NULL,
                    [venueId] int NOT NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_FAVORITE_VENUE_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [PK_FAVORITE_VENUE] PRIMARY KEY ([playerId], [venueId]),
                    CONSTRAINT [FK_FAVORITE_VENUE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER]([playerId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FAVORITE_VENUE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE]([venueId]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_FAVORITE_VENUE_venueId] ON [FAVORITE_VENUE] ([venueId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'RATING_HISTORY', N'bookingId') IS NULL ALTER TABLE [RATING_HISTORY] ADD [bookingId] int NULL;
            IF COL_LENGTH(N'RATING_HISTORY', N'comment') IS NULL ALTER TABLE [RATING_HISTORY] ADD [comment] nvarchar(1000) NULL;
            IF COL_LENGTH(N'RATING_HISTORY', N'tags') IS NULL ALTER TABLE [RATING_HISTORY] ADD [tags] nvarchar(500) NULL;
            IF COL_LENGTH(N'RATING_HISTORY', N'isAnonymous') IS NULL ALTER TABLE [RATING_HISTORY] ADD [isAnonymous] bit NOT NULL CONSTRAINT [DF_RATING_HISTORY_isAnonymous] DEFAULT (0);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RATING_HISTORY_BOOKING')
                ALTER TABLE [RATING_HISTORY] ADD CONSTRAINT [FK_RATING_HISTORY_BOOKING]
                FOREIGN KEY ([bookingId]) REFERENCES [BOOKING]([bookingId]) ON DELETE CASCADE;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_RATING_HISTORY_booking_user' AND object_id = OBJECT_ID(N'[RATING_HISTORY]'))
                CREATE UNIQUE INDEX [UQ_RATING_HISTORY_booking_user] ON [RATING_HISTORY] ([bookingId], [userId]) WHERE [bookingId] IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RATING_HISTORY_score' AND parent_object_id = OBJECT_ID(N'[RATING_HISTORY]', N'U'))
                ALTER TABLE [RATING_HISTORY] WITH CHECK ADD CONSTRAINT [CK_RATING_HISTORY_score] CHECK ([score] >= 1 AND [score] <= 5);
            ALTER TABLE [RATING_HISTORY] WITH CHECK CHECK CONSTRAINT [CK_RATING_HISTORY_score];
            """);
    }

    private static void EnsurePlayerMatchSchema(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.ExecuteSqlRaw("""
            IF COL_LENGTH(N'MATCH', N'hostPlayerId') IS NULL ALTER TABLE [MATCH] ADD [hostPlayerId] int NULL;
            IF COL_LENGTH(N'MATCH', N'requiredPlayerCount') IS NULL
                ALTER TABLE [MATCH] ADD [requiredPlayerCount] int NOT NULL CONSTRAINT [DF_MATCH_requiredPlayerCount] DEFAULT (2);
            IF COL_LENGTH(N'MATCH', N'note') IS NULL ALTER TABLE [MATCH] ADD [note] nvarchar(1000) NULL;
            IF COL_LENGTH(N'MATCH', N'title') IS NULL ALTER TABLE [MATCH] ADD [title] nvarchar(200) NULL;
            IF COL_LENGTH(N'MATCH', N'province') IS NULL ALTER TABLE [MATCH] ADD [province] nvarchar(100) NULL;
            IF COL_LENGTH(N'MATCH', N'ward') IS NULL ALTER TABLE [MATCH] ADD [ward] nvarchar(150) NULL;
            IF COL_LENGTH(N'MATCH', N'searchRadiusKm') IS NULL
                ALTER TABLE [MATCH] ADD [searchRadiusKm] float NOT NULL CONSTRAINT [DF_MATCH_searchRadiusKm] DEFAULT (5);
            IF COL_LENGTH(N'MATCH', N'searchLatitude') IS NULL ALTER TABLE [MATCH] ADD [searchLatitude] float NULL;
            IF COL_LENGTH(N'MATCH', N'searchLongitude') IS NULL ALTER TABLE [MATCH] ADD [searchLongitude] float NULL;
            IF COL_LENGTH(N'MATCH', N'availableDateFrom') IS NULL ALTER TABLE [MATCH] ADD [availableDateFrom] date NULL;
            IF COL_LENGTH(N'MATCH', N'availableDateTo') IS NULL ALTER TABLE [MATCH] ADD [availableDateTo] date NULL;
            IF COL_LENGTH(N'MATCH', N'minSkillLevel') IS NULL
                ALTER TABLE [MATCH] ADD [minSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_minSkillLevel] DEFAULT (1);
            IF COL_LENGTH(N'MATCH', N'maxSkillLevel') IS NULL
                ALTER TABLE [MATCH] ADD [maxSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_maxSkillLevel] DEFAULT (5);
            IF COL_LENGTH(N'MATCH', N'createdAt') IS NULL
                ALTER TABLE [MATCH] ADD [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_createdAt] DEFAULT (getutcdate());
            IF COL_LENGTH(N'MATCH', N'cancelledAt') IS NULL ALTER TABLE [MATCH] ADD [cancelledAt] datetime NULL;

            IF COL_LENGTH(N'MATCH_PARTICIPANT', N'status') IS NULL
                ALTER TABLE [MATCH_PARTICIPANT] ADD [status] nvarchar(30) NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_status] DEFAULT (N'Accepted');
            IF COL_LENGTH(N'MATCH_PARTICIPANT', N'isHost') IS NULL
                ALTER TABLE [MATCH_PARTICIPANT] ADD [isHost] bit NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_isHost] DEFAULT (0);
            IF COL_LENGTH(N'MATCH_PARTICIPANT', N'requestedAt') IS NULL
                ALTER TABLE [MATCH_PARTICIPANT] ADD [requestedAt] datetime NOT NULL CONSTRAINT [DF_MATCH_PARTICIPANT_requestedAt] DEFAULT (getutcdate());
            IF COL_LENGTH(N'MATCH_PARTICIPANT', N'respondedAt') IS NULL
                ALTER TABLE [MATCH_PARTICIPANT] ADD [respondedAt] datetime NULL;
            """);

        dbContext.Database.ExecuteSqlRaw("""
            UPDATE [MATCH]
            SET [requiredPlayerCount] = CASE
                WHEN LOWER(REPLACE([matchType], N' ', N'')) IN (N'2vs2', N'2v2') THEN 4
                ELSE 2
            END
            WHERE [availableDateFrom] IS NULL
            AND [requiredPlayerCount] <> CASE
                WHEN LOWER(REPLACE([matchType], N' ', N'')) IN (N'2vs2', N'2v2') THEN 4
                ELSE 2
            END;
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MATCH_HOST_PLAYER')
                ALTER TABLE [MATCH] ADD CONSTRAINT [FK_MATCH_HOST_PLAYER]
                FOREIGN KEY ([hostPlayerId]) REFERENCES [PLAYER]([playerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_PARTICIPANT_match_player' AND object_id = OBJECT_ID(N'[MATCH_PARTICIPANT]'))
            BEGIN
                ;WITH [DuplicateParticipants] AS (
                    SELECT [participantId],
                        ROW_NUMBER() OVER (
                            PARTITION BY [matchId], [playerId]
                            ORDER BY [isHost] DESC, [respondedAt] DESC, [participantId]
                        ) AS [rowNumber]
                    FROM [MATCH_PARTICIPANT]
                )
                DELETE FROM [MATCH_PARTICIPANT]
                WHERE [participantId] IN (
                    SELECT [participantId] FROM [DuplicateParticipants] WHERE [rowNumber] > 1
                );
                CREATE UNIQUE INDEX [UQ_MATCH_PARTICIPANT_match_player]
                    ON [MATCH_PARTICIPANT] ([matchId], [playerId]);
            END
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_MATCH_requiredPlayerCount' AND parent_object_id = OBJECT_ID(N'[MATCH]', N'U'))
                ALTER TABLE [MATCH] DROP CONSTRAINT [CK_MATCH_requiredPlayerCount];
            ALTER TABLE [MATCH] WITH CHECK ADD CONSTRAINT [CK_MATCH_requiredPlayerCount]
                CHECK ([requiredPlayerCount] BETWEEN 2 AND 4);
            ALTER TABLE [MATCH] WITH CHECK CHECK CONSTRAINT [CK_MATCH_requiredPlayerCount];
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[MATCH_PLAYER_REVIEW]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MATCH_PLAYER_REVIEW] (
                    [matchPlayerReviewId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_PLAYER_REVIEW] PRIMARY KEY,
                    [matchId] int NOT NULL,
                    [reviewerPlayerId] int NOT NULL,
                    [revieweePlayerId] int NOT NULL,
                    [score] int NOT NULL,
                    [comment] nvarchar(1000) NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_PLAYER_REVIEW_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWER] FOREIGN KEY ([reviewerPlayerId]) REFERENCES [PLAYER]([playerId]),
                    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWEE] FOREIGN KEY ([revieweePlayerId]) REFERENCES [PLAYER]([playerId]),
                    CONSTRAINT [CK_MATCH_PLAYER_REVIEW_score] CHECK ([score] >= 1 AND [score] <= 5),
                    CONSTRAINT [CK_MATCH_PLAYER_REVIEW_distinct_players] CHECK ([reviewerPlayerId] <> [revieweePlayerId])
                );
                CREATE UNIQUE INDEX [UQ_MATCH_PLAYER_REVIEW]
                    ON [MATCH_PLAYER_REVIEW] ([matchId], [reviewerPlayerId], [revieweePlayerId]);
                CREATE INDEX [IX_MATCH_PLAYER_REVIEW_revieweePlayerId]
                    ON [MATCH_PLAYER_REVIEW] ([revieweePlayerId]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MATCH_AVAILABILITY_SLOT] (
                    [matchAvailabilitySlotId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_AVAILABILITY_SLOT] PRIMARY KEY,
                    [matchId] int NOT NULL,
                    [timeStart] time NOT NULL,
                    [timeEnd] time NOT NULL,
                    CONSTRAINT [FK_MATCH_AVAILABILITY_SLOT_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                    CONSTRAINT [CK_MATCH_AVAILABILITY_SLOT_time] CHECK ([timeEnd] > [timeStart])
                );
                CREATE INDEX [IX_MATCH_AVAILABILITY_SLOT_matchId]
                    ON [MATCH_AVAILABILITY_SLOT] ([matchId]);
                CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                    ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
            END
            IF COL_LENGTH(N'MATCH_AVAILABILITY_SLOT', N'availableDate') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                    DROP INDEX [UQ_MATCH_AVAILABILITY_SLOT] ON [MATCH_AVAILABILITY_SLOT];
                ALTER TABLE [MATCH_AVAILABILITY_SLOT] DROP COLUMN [availableDate];
            END
            ;WITH [DuplicateSlots] AS (
                SELECT [matchAvailabilitySlotId],
                    ROW_NUMBER() OVER (
                        PARTITION BY [matchId], [timeStart], [timeEnd]
                        ORDER BY [matchAvailabilitySlotId]
                    ) AS [rowNumber]
                FROM [MATCH_AVAILABILITY_SLOT]
            )
            DELETE FROM [MATCH_AVAILABILITY_SLOT]
            WHERE [matchAvailabilitySlotId] IN (
                SELECT [matchAvailabilitySlotId] FROM [DuplicateSlots] WHERE [rowNumber] > 1
            );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                    ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MATCH_SLOT_VOTE] (
                    [matchSlotVoteId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_SLOT_VOTE] PRIMARY KEY,
                    [matchId] int NOT NULL,
                    [playerId] int NOT NULL,
                    [courtId] int NOT NULL,
                    [startTime] datetime NOT NULL,
                    [endTime] datetime NOT NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_SLOT_VOTE_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER]([playerId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT]([courtId]) ON DELETE NO ACTION,
                    CONSTRAINT [CK_MATCH_SLOT_VOTE_time] CHECK ([endTime] > [startTime])
                );
                CREATE INDEX [IX_MATCH_SLOT_VOTE_matchId]
                    ON [MATCH_SLOT_VOTE] ([matchId]);
                CREATE INDEX [IX_MATCH_SLOT_VOTE_court_time]
                    ON [MATCH_SLOT_VOTE] ([courtId], [startTime], [endTime]);
                CREATE UNIQUE INDEX [UQ_MATCH_SLOT_VOTE_player_slot]
                    ON [MATCH_SLOT_VOTE] ([matchId], [playerId], [courtId], [startTime], [endTime]);
            END
            """);

        dbContext.Database.ExecuteSqlRaw("""
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CONV_PARTICIPANT_CONVERSATION')
            BEGIN
                ALTER TABLE [CONVERSATION_PARTICIPANT] DROP CONSTRAINT [FK_CONV_PARTICIPANT_CONVERSATION];
                ALTER TABLE [CONVERSATION_PARTICIPANT] ADD CONSTRAINT [FK_CONV_PARTICIPANT_CONVERSATION]
                    FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]) ON DELETE CASCADE;
            END

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MESSAGE_CONVERSATION')
            BEGIN
                ALTER TABLE [MESSAGE] DROP CONSTRAINT [FK_MESSAGE_CONVERSATION];
                ALTER TABLE [MESSAGE] ADD CONSTRAINT [FK_MESSAGE_CONVERSATION]
                    FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION]([conversationId]) ON DELETE CASCADE;
            END
            """);
    }
}
