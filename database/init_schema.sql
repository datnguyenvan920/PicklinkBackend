IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [TOURNAMENT] (
    [tournamentId] int NOT NULL IDENTITY,
    [name] nvarchar(200) NOT NULL,
    [startDate] date NOT NULL,
    [endDate] date NOT NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Upcoming',
    CONSTRAINT [PK_TOURNAMENT] PRIMARY KEY ([tournamentId])
);
GO

CREATE TABLE [USER] (
    [userId] int NOT NULL IDENTITY,
    [username] nvarchar(100) NOT NULL,
    [email] nvarchar(255) NOT NULL,
    [passwordHash] nvarchar(512) NOT NULL,
    [userType] nvarchar(50) NOT NULL,
    [profileImageUrl] nvarchar(500) NULL,
    [city] nvarchar(100) NULL,
    [commune] nvarchar(150) NULL,
    CONSTRAINT [PK_USER] PRIMARY KEY ([userId])
);
GO

CREATE TABLE [FRIENDSHIP] (
    [friendshipId] int NOT NULL IDENTITY,
    [requesterId] int NOT NULL,
    [receiverId] int NOT NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Pending',
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    [updatedAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_FRIENDSHIP] PRIMARY KEY ([friendshipId]),
    CONSTRAINT [FK_FRIENDSHIP_RECEIVER] FOREIGN KEY ([receiverId]) REFERENCES [USER] ([userId]),
    CONSTRAINT [FK_FRIENDSHIP_REQUESTER] FOREIGN KEY ([requesterId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [MARKETPLACE_PROVIDER] (
    [providerId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [specialty] nvarchar(200) NULL,
    [providerType] nvarchar(100) NULL,
    CONSTRAINT [PK_MARKETPLACE_PROVIDER] PRIMARY KEY ([providerId]),
    CONSTRAINT [FK_MARKETPLACE_PROVIDER_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [NOTIFICATION_LOG] (
    [notifId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [message] nvarchar(max) NOT NULL,
    [isRead] bit NOT NULL,
    CONSTRAINT [PK_NOTIFICATION_LOG] PRIMARY KEY ([notifId]),
    CONSTRAINT [FK_NOTIFICATION_LOG_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [PASSWORD_RESET_TOKEN] (
    [resetTokenId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [tokenHash] nvarchar(128) NOT NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    [expiresAt] datetime NOT NULL,
    [usedAt] datetime NULL,
    CONSTRAINT [PK_PASSWORD_RESET_TOKEN] PRIMARY KEY ([resetTokenId]),
    CONSTRAINT [FK_PASSWORD_RESET_TOKEN_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [PLAYER] (
    [playerId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [prestige] float NOT NULL DEFAULT 5.0E0,
    [skillLevel] float NOT NULL,
    [playerSubType] nvarchar(50) NULL,
    [playFrequency] nvarchar(50) NULL,
    [preferredTimeSlot] nvarchar(50) NULL,
    [bio] nvarchar(500) NULL,
    [birthDate] date NULL,
    [gender] nvarchar(30) NULL,
    [heightCm] float NULL,
    [weightKg] float NULL,
    CONSTRAINT [PK_PLAYER] PRIMARY KEY ([playerId]),
    CONSTRAINT [FK_PLAYER_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [VENUE_OWNER] (
    [ownerId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [specialPermissions] text NULL,
    CONSTRAINT [PK_VENUE_OWNER] PRIMARY KEY ([ownerId]),
    CONSTRAINT [FK_VENUE_OWNER_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [INVENTORY_ITEM] (
    [itemId] int NOT NULL IDENTITY,
    [providerId] int NOT NULL,
    [itemName] nvarchar(200) NOT NULL,
    [pricePerUnit] float NOT NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Available',
    CONSTRAINT [PK_INVENTORY_ITEM] PRIMARY KEY ([itemId]),
    CONSTRAINT [FK_INVENTORY_ITEM_PROVIDER] FOREIGN KEY ([providerId]) REFERENCES [MARKETPLACE_PROVIDER] ([providerId])
);
GO

CREATE TABLE [SOCIAL_GROUP] (
    [groupId] int NOT NULL IDENTITY,
    [ownerId] int NOT NULL,
    [groupName] nvarchar(200) NOT NULL,
    [description] nvarchar(max) NULL,
    [groupType] nvarchar(50) NOT NULL DEFAULT N'Public',
    [coverImageUrl] nvarchar(500) NULL,
    [rules] nvarchar(max) NULL,
    [overallRating] float NOT NULL DEFAULT 0.0E0,
    [ratingCount] int NOT NULL DEFAULT 0,
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_SOCIAL_GROUP] PRIMARY KEY ([groupId]),
    CONSTRAINT [FK_SOCIAL_GROUP_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [TEAM] (
    [teamId] int NOT NULL IDENTITY,
    [teamName] nvarchar(200) NOT NULL,
    [captainId] int NOT NULL,
    [description] nvarchar(500) NULL,
    CONSTRAINT [PK_TEAM] PRIMARY KEY ([teamId]),
    CONSTRAINT [FK_TEAM_CAPTAIN] FOREIGN KEY ([captainId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [OWNER_BANK_ACCOUNT] (
    [ownerBankAccountId] int NOT NULL IDENTITY,
    [ownerId] int NOT NULL,
    [bankCode] nvarchar(30) NOT NULL,
    [bankName] nvarchar(150) NOT NULL,
    [accountNumber] nvarchar(50) NOT NULL,
    [accountHolderName] nvarchar(200) NOT NULL,
    [isActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [createdAt] datetime NOT NULL,
    [updatedAt] datetime NOT NULL,
    CONSTRAINT [PK_OWNER_BANK_ACCOUNT] PRIMARY KEY ([ownerBankAccountId]),
    CONSTRAINT [FK_OWNER_BANK_ACCOUNT_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [VENUE_OWNER] ([ownerId]) ON DELETE CASCADE
);
GO

CREATE TABLE [VENUE] (
    [venueId] int NOT NULL IDENTITY,
    [ownerId] int NOT NULL,
    [venueName] nvarchar(200) NOT NULL,
    [address] nvarchar(500) NOT NULL,
    [overallRating] float NOT NULL,
    [openTime] time NOT NULL,
    [closeTime] time NOT NULL,
    [phoneNumber] nvarchar(20) NULL,
    [latitude] float NULL,
    [longitude] float NULL,
    [isOpen] bit NOT NULL DEFAULT CAST(1 AS bit),
    [approvalStatus] nvarchar(30) NOT NULL DEFAULT N'Draft',
    [rejectionReason] nvarchar(500) NULL,
    CONSTRAINT [PK_VENUE] PRIMARY KEY ([venueId]),
    CONSTRAINT [FK_VENUE_OWNER] FOREIGN KEY ([ownerId]) REFERENCES [VENUE_OWNER] ([ownerId])
);
GO

CREATE TABLE [GROUP_IMAGE] (
    [groupImageId] int NOT NULL IDENTITY,
    [groupId] int NOT NULL,
    [imageUrl] nvarchar(1000) NOT NULL,
    [caption] nvarchar(200) NULL,
    [sortOrder] int NOT NULL DEFAULT 0,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    CONSTRAINT [PK_GROUP_IMAGE] PRIMARY KEY ([groupImageId]),
    CONSTRAINT [FK_GROUP_IMAGE_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP] ([groupId]) ON DELETE CASCADE
);
GO

CREATE TABLE [GROUP_MEMBER] (
    [groupId] int NOT NULL,
    [userId] int NOT NULL,
    [role] nvarchar(50) NOT NULL DEFAULT N'Member',
    [status] nvarchar(50) NOT NULL DEFAULT N'Accepted',
    [joinedAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_GROUP_MEMBER] PRIMARY KEY ([groupId], [userId]),
    CONSTRAINT [FK_GROUP_MEMBER_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP] ([groupId]),
    CONSTRAINT [FK_GROUP_MEMBER_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [POST] (
    [postId] int NOT NULL IDENTITY,
    [authorId] int NOT NULL,
    [groupId] int NULL,
    [content] nvarchar(max) NULL,
    [postType] nvarchar(50) NOT NULL DEFAULT N'Post',
    [visibility] nvarchar(50) NOT NULL DEFAULT N'Public',
    [expiresAt] datetime NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    [updatedAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_POST] PRIMARY KEY ([postId]),
    CONSTRAINT [FK_POST_AUTHOR] FOREIGN KEY ([authorId]) REFERENCES [USER] ([userId]),
    CONSTRAINT [FK_POST_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP] ([groupId])
);
GO

CREATE TABLE [MATCH] (
    [matchId] int NOT NULL IDENTITY,
    [hostPlayerId] int NULL,
    [matchType] nvarchar(100) NOT NULL,
    [matchSkillLevel] int NOT NULL,
    [requiredPlayerCount] int NOT NULL DEFAULT 2,
    [matchTime] datetime NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Scheduled',
    [note] nvarchar(1000) NULL,
    [createdAt] datetime NOT NULL,
    [cancelledAt] datetime NULL,
    [preferredTimeStart] time NULL,
    [preferredTimeEnd] time NULL,
    [sharedVenues] nvarchar(500) NULL,
    [team1Id] int NULL,
    [team2Id] int NULL,
    [winningTeamId] int NULL,
    CONSTRAINT [PK_MATCH] PRIMARY KEY ([matchId]),
    CONSTRAINT [FK_MATCH_HOST_PLAYER] FOREIGN KEY ([hostPlayerId]) REFERENCES [PLAYER] ([playerId]),
    CONSTRAINT [FK_MATCH_TEAM1] FOREIGN KEY ([team1Id]) REFERENCES [TEAM] ([teamId]),
    CONSTRAINT [FK_MATCH_TEAM2] FOREIGN KEY ([team2Id]) REFERENCES [TEAM] ([teamId]),
    CONSTRAINT [FK_MATCH_WINNER] FOREIGN KEY ([winningTeamId]) REFERENCES [TEAM] ([teamId])
);
GO

CREATE TABLE [PLAYER_TEAM_ROSTER] (
    [playerId] int NOT NULL,
    [teamId] int NOT NULL,
    [joinedDate] date NOT NULL DEFAULT ((CONVERT([date],getdate()))),
    CONSTRAINT [PK_PLAYER_TEAM_ROSTER] PRIMARY KEY ([playerId], [teamId]),
    CONSTRAINT [FK_PTR_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]),
    CONSTRAINT [FK_PTR_TEAM] FOREIGN KEY ([teamId]) REFERENCES [TEAM] ([teamId])
);
GO

CREATE TABLE [TOURNAMENT_TEAM] (
    [tournamentId] int NOT NULL,
    [teamId] int NOT NULL,
    CONSTRAINT [PK_TOURNAMENT_TEAM] PRIMARY KEY ([tournamentId], [teamId]),
    CONSTRAINT [FK_TOURNAMENT_TEAM_TEAM] FOREIGN KEY ([teamId]) REFERENCES [TEAM] ([teamId]),
    CONSTRAINT [FK_TOURNAMENT_TEAM_TOURN] FOREIGN KEY ([tournamentId]) REFERENCES [TOURNAMENT] ([tournamentId])
);
GO

CREATE TABLE [AMENITY] (
    [amenityId] int NOT NULL IDENTITY,
    [venueId] int NOT NULL,
    [amenityName] nvarchar(200) NOT NULL,
    [isFree] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_AMENITY] PRIMARY KEY ([amenityId]),
    CONSTRAINT [FK_AMENITY_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId])
);
GO

CREATE TABLE [BOOKING_RULES] (
    [ruleId] int NOT NULL IDENTITY,
    [venueId] int NOT NULL,
    [ruleType] nvarchar(100) NOT NULL,
    [ruleContent] text NOT NULL,
    CONSTRAINT [PK_BOOKING_RULES] PRIMARY KEY ([ruleId]),
    CONSTRAINT [FK_BOOKING_RULES_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId])
);
GO

CREATE TABLE [COURT] (
    [courtId] int NOT NULL IDENTITY,
    [venueId] int NOT NULL,
    [courtNumber] int NOT NULL,
    [surfaceType] nvarchar(100) NULL,
    [courtType] nvarchar(100) NULL DEFAULT N'Standard',
    [hourlyPrice] float NOT NULL,
    [isIndoor] bit NOT NULL,
    [availabilityStatus] nvarchar(50) NOT NULL DEFAULT N'Available',
    CONSTRAINT [PK_COURT] PRIMARY KEY ([courtId]),
    CONSTRAINT [FK_COURT_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId])
);
GO

CREATE TABLE [FAVORITE_VENUE] (
    [playerId] int NOT NULL,
    [venueId] int NOT NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    CONSTRAINT [PK_FAVORITE_VENUE] PRIMARY KEY ([playerId], [venueId]),
    CONSTRAINT [FK_FAVORITE_VENUE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]) ON DELETE CASCADE,
    CONSTRAINT [FK_FAVORITE_VENUE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId]) ON DELETE CASCADE
);
GO

CREATE TABLE [STAFF] (
    [staffId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [venueId] int NOT NULL,
    [role] nvarchar(100) NOT NULL,
    [permissions] nvarchar(500) NOT NULL,
    [isActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [assignedAt] datetime NOT NULL,
    [assignedByUserId] int NULL,
    [revokedAt] datetime NULL,
    CONSTRAINT [PK_STAFF] PRIMARY KEY ([staffId]),
    CONSTRAINT [FK_STAFF_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId]),
    CONSTRAINT [FK_STAFF_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId])
);
GO

CREATE TABLE [VENUE_AUDIT_LOG] (
    [logId] int NOT NULL IDENTITY,
    [venueId] int NOT NULL,
    [actorId] int NOT NULL,
    [action] nvarchar(500) NOT NULL,
    [timestamp] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_VENUE_AUDIT_LOG] PRIMARY KEY ([logId]),
    CONSTRAINT [FK_VENUE_AUDIT_LOG_ACTOR] FOREIGN KEY ([actorId]) REFERENCES [USER] ([userId]),
    CONSTRAINT [FK_VENUE_AUDIT_LOG_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId])
);
GO

CREATE TABLE [VENUE_IMAGE] (
    [venueImageId] int NOT NULL IDENTITY,
    [venueId] int NOT NULL,
    [imageUrl] nvarchar(1000) NOT NULL,
    [caption] nvarchar(200) NULL,
    [isPrimary] bit NOT NULL,
    [sortOrder] int NOT NULL,
    [createdAt] datetime NOT NULL,
    CONSTRAINT [PK_VENUE_IMAGE] PRIMARY KEY ([venueImageId]),
    CONSTRAINT [FK_VENUE_IMAGE_VENUE] FOREIGN KEY ([venueId]) REFERENCES [VENUE] ([venueId]) ON DELETE CASCADE
);
GO

CREATE TABLE [POST_COMMENT] (
    [commentId] int NOT NULL IDENTITY,
    [postId] int NOT NULL,
    [userId] int NOT NULL,
    [parentCommentId] int NULL,
    [content] nvarchar(max) NOT NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    [updatedAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_POST_COMMENT] PRIMARY KEY ([commentId]),
    CONSTRAINT [FK_POST_COMMENT_PARENT] FOREIGN KEY ([parentCommentId]) REFERENCES [POST_COMMENT] ([commentId]),
    CONSTRAINT [FK_POST_COMMENT_POST] FOREIGN KEY ([postId]) REFERENCES [POST] ([postId]),
    CONSTRAINT [FK_POST_COMMENT_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [POST_LIKE] (
    [likeId] int NOT NULL IDENTITY,
    [postId] int NOT NULL,
    [userId] int NOT NULL,
    [reactionType] nvarchar(50) NOT NULL DEFAULT N'Like',
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_POST_LIKE] PRIMARY KEY ([likeId]),
    CONSTRAINT [FK_POST_LIKE_POST] FOREIGN KEY ([postId]) REFERENCES [POST] ([postId]),
    CONSTRAINT [FK_POST_LIKE_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [POST_MEDIA] (
    [mediaId] int NOT NULL IDENTITY,
    [postId] int NOT NULL,
    [mediaUrl] nvarchar(500) NOT NULL,
    [mediaType] nvarchar(50) NOT NULL DEFAULT N'Image',
    [displayOrder] int NOT NULL,
    CONSTRAINT [PK_POST_MEDIA] PRIMARY KEY ([mediaId]),
    CONSTRAINT [FK_POST_MEDIA_POST] FOREIGN KEY ([postId]) REFERENCES [POST] ([postId])
);
GO

CREATE TABLE [CONVERSATION] (
    [conversationId] int NOT NULL IDENTITY,
    [groupId] int NULL,
    [matchId] int NULL,
    [conversationType] nvarchar(50) NOT NULL DEFAULT N'Direct',
    [conversationName] nvarchar(200) NULL,
    [lastMessageAt] datetime NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_CONVERSATION] PRIMARY KEY ([conversationId]),
    CONSTRAINT [FK_CONVERSATION_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]),
    CONSTRAINT [FK_CONVERSATION_SOCIAL_GROUP] FOREIGN KEY ([groupId]) REFERENCES [SOCIAL_GROUP] ([groupId])
);
GO

CREATE TABLE [MATCH_PARTICIPANT] (
    [participantId] int NOT NULL IDENTITY,
    [matchId] int NOT NULL,
    [playerId] int NOT NULL,
    [class] nvarchar(100) NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Accepted',
    [isHost] bit NOT NULL DEFAULT CAST(0 AS bit),
    [requestedAt] datetime NOT NULL,
    [respondedAt] datetime NULL,
    [votedVenueId] int NULL,
    [votedStartTime] time NULL,
    [votedEndTime] time NULL,
    CONSTRAINT [PK_MATCH_PARTICIPANT] PRIMARY KEY ([participantId]),
    CONSTRAINT [FK_MATCH_PARTICIPANT_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]),
    CONSTRAINT [FK_MATCH_PARTICIPANT_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [MATCH_PLAYER_REVIEW] (
    [matchPlayerReviewId] int NOT NULL IDENTITY,
    [matchId] int NOT NULL,
    [reviewerPlayerId] int NOT NULL,
    [revieweePlayerId] int NOT NULL,
    [score] int NOT NULL,
    [comment] nvarchar(1000) NULL,
    [createdAt] datetime NOT NULL,
    CONSTRAINT [PK_MATCH_PLAYER_REVIEW] PRIMARY KEY ([matchPlayerReviewId]),
    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWEE] FOREIGN KEY ([revieweePlayerId]) REFERENCES [PLAYER] ([playerId]),
    CONSTRAINT [FK_MATCH_PLAYER_REVIEW_REVIEWER] FOREIGN KEY ([reviewerPlayerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [SKILL_MATCHUP] (
    [matchupId] int NOT NULL IDENTITY,
    [playerId] int NOT NULL,
    [matchId] int NOT NULL,
    [skillDelta] int NOT NULL,
    CONSTRAINT [PK_SKILL_MATCHUP] PRIMARY KEY ([matchupId]),
    CONSTRAINT [FK_SKILL_MATCHUP_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]),
    CONSTRAINT [FK_SKILL_MATCHUP_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [BOOKING] (
    [bookingId] int NOT NULL IDENTITY,
    [playerId] int NULL,
    [courtId] int NOT NULL,
    [matchId] int NULL,
    [startTime] datetime NOT NULL,
    [endTime] datetime NOT NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Pending',
    [ownerEntryType] nvarchar(30) NULL,
    [title] nvarchar(200) NULL,
    [bookingCode] nvarchar(30) NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    [holdExpiresAt] datetime NULL,
    [hourlyPriceSnapshot] float NOT NULL,
    [courtAmount] float NOT NULL,
    [totalAmount] float NOT NULL,
    CONSTRAINT [PK_BOOKING] PRIMARY KEY ([bookingId]),
    CONSTRAINT [FK_BOOKING_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT] ([courtId]),
    CONSTRAINT [FK_BOOKING_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]),
    CONSTRAINT [FK_BOOKING_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [SCORECARD] (
    [gameId] int NOT NULL IDENTITY,
    [matchId] int NOT NULL,
    [courtId] int NOT NULL,
    [scoreInfo] nvarchar(max) NULL,
    CONSTRAINT [PK_SCORECARD] PRIMARY KEY ([gameId]),
    CONSTRAINT [FK_SCORECARD_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT] ([courtId]),
    CONSTRAINT [FK_SCORECARD_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId])
);
GO

CREATE TABLE [MATCH_CHECKIN] (
    [checkinId] int NOT NULL IDENTITY,
    [matchId] int NOT NULL,
    [playerId] int NOT NULL,
    [staffId] int NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Present',
    [checkedInAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_MATCH_CHECKIN] PRIMARY KEY ([checkinId]),
    CONSTRAINT [FK_MATCH_CHECKIN_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]),
    CONSTRAINT [FK_MATCH_CHECKIN_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]),
    CONSTRAINT [FK_MATCH_CHECKIN_STAFF] FOREIGN KEY ([staffId]) REFERENCES [STAFF] ([staffId])
);
GO

CREATE TABLE [CONVERSATION_PARTICIPANT] (
    [conversationId] int NOT NULL,
    [userId] int NOT NULL,
    [joinedAt] datetime NOT NULL DEFAULT ((getdate())),
    [lastReadAt] datetime NULL,
    CONSTRAINT [PK_CONVERSATION_PARTICIPANT] PRIMARY KEY ([conversationId], [userId]),
    CONSTRAINT [FK_CONV_PARTICIPANT_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION] ([conversationId]),
    CONSTRAINT [FK_CONV_PARTICIPANT_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [MESSAGE] (
    [messageId] int NOT NULL IDENTITY,
    [conversationId] int NOT NULL,
    [senderId] int NOT NULL,
    [content] nvarchar(max) NULL,
    [messageType] nvarchar(50) NOT NULL DEFAULT N'Text',
    [mediaUrl] nvarchar(500) NULL,
    [replyToMessageId] int NULL,
    [sentAt] datetime NOT NULL DEFAULT ((getdate())),
    [isDeleted] bit NOT NULL,
    CONSTRAINT [PK_MESSAGE] PRIMARY KEY ([messageId]),
    CONSTRAINT [FK_MESSAGE_CONVERSATION] FOREIGN KEY ([conversationId]) REFERENCES [CONVERSATION] ([conversationId]),
    CONSTRAINT [FK_MESSAGE_REPLY] FOREIGN KEY ([replyToMessageId]) REFERENCES [MESSAGE] ([messageId]),
    CONSTRAINT [FK_MESSAGE_SENDER] FOREIGN KEY ([senderId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [BOOKING_OPERATION] (
    [bookingOperationId] int NOT NULL IDENTITY,
    [bookingId] int NOT NULL,
    [checkInStatus] nvarchar(30) NOT NULL DEFAULT N'Ready',
    [codeVerifiedAt] datetime NULL,
    [codeVerifiedByUserId] int NULL,
    [paymentConfirmedAt] datetime NULL,
    [paymentConfirmedByUserId] int NULL,
    [checkedInAt] datetime NULL,
    [checkedInByUserId] int NULL,
    [noShowAt] datetime NULL,
    [noShowByUserId] int NULL,
    [updatedAt] datetime NOT NULL,
    CONSTRAINT [PK_BOOKING_OPERATION] PRIMARY KEY ([bookingOperationId]),
    CONSTRAINT [FK_BOOKING_OPERATION_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE CASCADE
);
GO

CREATE TABLE [BOOKING_STATUS_HISTORY] (
    [bookingStatusHistoryId] int NOT NULL IDENTITY,
    [bookingId] int NOT NULL,
    [fromStatus] nvarchar(50) NULL,
    [toStatus] nvarchar(50) NOT NULL,
    [reason] nvarchar(500) NULL,
    [actorUserId] int NULL,
    [changedAt] datetime NOT NULL,
    CONSTRAINT [PK_BOOKING_STATUS_HISTORY] PRIMARY KEY ([bookingStatusHistoryId]),
    CONSTRAINT [FK_BOOKING_STATUS_HISTORY_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE CASCADE
);
GO

CREATE TABLE [PAYMENT] (
    [paymentId] int NOT NULL IDENTITY,
    [bookingId] int NOT NULL,
    [payerId] int NOT NULL,
    [amount] float NOT NULL,
    [paymentMethod] nvarchar(100) NOT NULL,
    [status] nvarchar(50) NOT NULL DEFAULT N'Pending',
    [paidAt] datetime NULL,
    [transferCode] nvarchar(40) NULL,
    [transferContent] nvarchar(140) NULL,
    [bankCode] nvarchar(30) NULL,
    [bankName] nvarchar(150) NULL,
    [bankAccountNumber] nvarchar(50) NULL,
    [bankAccountName] nvarchar(200) NULL,
    [qrImageUrl] nvarchar(2000) NULL,
    [receiptImageUrl] nvarchar(1000) NULL,
    [submittedAt] datetime NULL,
    [verifiedAt] datetime NULL,
    [verifiedByUserId] int NULL,
    [rejectionReason] nvarchar(500) NULL,
    CONSTRAINT [PK_PAYMENT] PRIMARY KEY ([paymentId]),
    CONSTRAINT [FK_PAYMENT_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]),
    CONSTRAINT [FK_PAYMENT_PAYER] FOREIGN KEY ([payerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [RATING_HISTORY] (
    [ratingId] int NOT NULL IDENTITY,
    [userId] int NOT NULL,
    [bookingId] int NULL,
    [targetId] int NOT NULL,
    [targetType] nvarchar(50) NOT NULL,
    [score] int NOT NULL,
    [comment] nvarchar(1000) NULL,
    [tags] nvarchar(500) NULL,
    [isAnonymous] bit NOT NULL DEFAULT CAST(0 AS bit),
    [createdAt] datetime NOT NULL DEFAULT ((getdate())),
    CONSTRAINT [PK_RATING_HISTORY] PRIMARY KEY ([ratingId]),
    CONSTRAINT [FK_RATING_HISTORY_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE CASCADE,
    CONSTRAINT [FK_RATING_HISTORY_USER] FOREIGN KEY ([userId]) REFERENCES [USER] ([userId])
);
GO

CREATE TABLE [PAYMENT_STATUS_HISTORY] (
    [paymentStatusHistoryId] int NOT NULL IDENTITY,
    [paymentId] int NOT NULL,
    [fromStatus] nvarchar(50) NULL,
    [toStatus] nvarchar(50) NOT NULL,
    [action] nvarchar(100) NOT NULL,
    [reason] nvarchar(500) NULL,
    [actorUserId] int NULL,
    [createdAt] datetime NOT NULL,
    CONSTRAINT [PK_PAYMENT_STATUS_HISTORY] PRIMARY KEY ([paymentStatusHistoryId]),
    CONSTRAINT [FK_PAYMENT_STATUS_HISTORY_PAYMENT] FOREIGN KEY ([paymentId]) REFERENCES [PAYMENT] ([paymentId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_AMENITY_venueId] ON [AMENITY] ([venueId]);
GO

CREATE INDEX [IX_BOOKING_courtId] ON [BOOKING] ([courtId]);
GO

CREATE INDEX [IX_BOOKING_matchId] ON [BOOKING] ([matchId]);
GO

CREATE INDEX [IX_BOOKING_playerId] ON [BOOKING] ([playerId]);
GO

CREATE INDEX [IX_BOOKING_startTime] ON [BOOKING] ([startTime]);
GO

CREATE UNIQUE INDEX [UQ_BOOKING_OPERATION_bookingId] ON [BOOKING_OPERATION] ([bookingId]);
GO

CREATE INDEX [IX_BOOKING_RULES_venueId] ON [BOOKING_RULES] ([venueId]);
GO

CREATE INDEX [IX_BOOKING_STATUS_HISTORY_bookingId] ON [BOOKING_STATUS_HISTORY] ([bookingId]);
GO

CREATE INDEX [IX_CONVERSATION_groupId] ON [CONVERSATION] ([groupId]) WHERE ([groupId] IS NOT NULL);
GO

CREATE INDEX [IX_CONVERSATION_lastMessageAt] ON [CONVERSATION] ([lastMessageAt] DESC);
GO

CREATE INDEX [IX_CONVERSATION_matchId] ON [CONVERSATION] ([matchId]);
GO

CREATE INDEX [IX_CONV_PARTICIPANT_userId] ON [CONVERSATION_PARTICIPANT] ([userId]);
GO

CREATE INDEX [IX_COURT_venueId] ON [COURT] ([venueId]);
GO

CREATE INDEX [IX_FAVORITE_VENUE_venueId] ON [FAVORITE_VENUE] ([venueId]);
GO

CREATE INDEX [IX_FRIENDSHIP_receiver] ON [FRIENDSHIP] ([receiverId], [status]);
GO

CREATE INDEX [IX_FRIENDSHIP_requester] ON [FRIENDSHIP] ([requesterId], [status]);
GO

CREATE UNIQUE INDEX [UQ_FRIENDSHIP_PAIR] ON [FRIENDSHIP] ([requesterId], [receiverId]);
GO

CREATE INDEX [IX_GROUP_IMAGE_groupId] ON [GROUP_IMAGE] ([groupId], [sortOrder]);
GO

CREATE INDEX [IX_GROUP_MEMBER_userId] ON [GROUP_MEMBER] ([userId], [status]);
GO

CREATE INDEX [IX_INVENTORY_ITEM_providerId] ON [INVENTORY_ITEM] ([providerId]);
GO

CREATE INDEX [IX_MARKETPLACE_PROVIDER_userId] ON [MARKETPLACE_PROVIDER] ([userId]);
GO

CREATE INDEX [IX_MATCH_hostPlayerId] ON [MATCH] ([hostPlayerId]);
GO

CREATE INDEX [IX_MATCH_matchTime] ON [MATCH] ([matchTime]);
GO

CREATE INDEX [IX_MATCH_status] ON [MATCH] ([status]);
GO

CREATE INDEX [IX_MATCH_team1Id] ON [MATCH] ([team1Id]);
GO

CREATE INDEX [IX_MATCH_team2Id] ON [MATCH] ([team2Id]);
GO

CREATE INDEX [IX_MATCH_winningTeamId] ON [MATCH] ([winningTeamId]);
GO

CREATE INDEX [IX_MATCH_CHECKIN_matchId] ON [MATCH_CHECKIN] ([matchId]);
GO

CREATE INDEX [IX_MATCH_CHECKIN_playerId] ON [MATCH_CHECKIN] ([playerId]);
GO

CREATE INDEX [IX_MATCH_CHECKIN_staffId] ON [MATCH_CHECKIN] ([staffId]);
GO

CREATE UNIQUE INDEX [UQ_MATCH_CHECKIN_UNIQUE] ON [MATCH_CHECKIN] ([matchId], [playerId]);
GO

CREATE INDEX [IX_MATCH_PARTICIPANT_match] ON [MATCH_PARTICIPANT] ([matchId]);
GO

CREATE INDEX [IX_MATCH_PARTICIPANT_player] ON [MATCH_PARTICIPANT] ([playerId]);
GO

CREATE UNIQUE INDEX [UQ_MATCH_PARTICIPANT_match_player] ON [MATCH_PARTICIPANT] ([matchId], [playerId]);
GO

CREATE INDEX [IX_MATCH_PLAYER_REVIEW_revieweePlayerId] ON [MATCH_PLAYER_REVIEW] ([revieweePlayerId]);
GO

CREATE INDEX [IX_MATCH_PLAYER_REVIEW_reviewerPlayerId] ON [MATCH_PLAYER_REVIEW] ([reviewerPlayerId]);
GO

CREATE UNIQUE INDEX [UQ_MATCH_PLAYER_REVIEW] ON [MATCH_PLAYER_REVIEW] ([matchId], [reviewerPlayerId], [revieweePlayerId]);
GO

CREATE INDEX [IX_MESSAGE_conversationId] ON [MESSAGE] ([conversationId], [sentAt] DESC);
GO

CREATE INDEX [IX_MESSAGE_replyToMessageId] ON [MESSAGE] ([replyToMessageId]);
GO

CREATE INDEX [IX_MESSAGE_senderId] ON [MESSAGE] ([senderId]);
GO

CREATE INDEX [IX_NOTIF_userId] ON [NOTIFICATION_LOG] ([userId]);
GO

CREATE UNIQUE INDEX [UQ_OWNER_BANK_ACCOUNT_ownerId] ON [OWNER_BANK_ACCOUNT] ([ownerId]);
GO

CREATE INDEX [IX_PASSWORD_RESET_TOKEN_tokenHash] ON [PASSWORD_RESET_TOKEN] ([tokenHash]);
GO

CREATE INDEX [IX_PASSWORD_RESET_TOKEN_userId] ON [PASSWORD_RESET_TOKEN] ([userId]);
GO

CREATE INDEX [IX_PAYMENT_bookingId] ON [PAYMENT] ([bookingId]);
GO

CREATE INDEX [IX_PAYMENT_payerId] ON [PAYMENT] ([payerId]);
GO

CREATE UNIQUE INDEX [UQ_PAYMENT_transferCode] ON [PAYMENT] ([transferCode]) WHERE [transferCode] IS NOT NULL;
GO

CREATE INDEX [IX_PAYMENT_STATUS_HISTORY_paymentId] ON [PAYMENT_STATUS_HISTORY] ([paymentId]);
GO

CREATE INDEX [IX_PLAYER_userId] ON [PLAYER] ([userId]);
GO

CREATE INDEX [IX_PLAYER_TEAM_ROSTER_teamId] ON [PLAYER_TEAM_ROSTER] ([teamId]);
GO

CREATE INDEX [IX_POST_authorId] ON [POST] ([authorId]);
GO

CREATE INDEX [IX_POST_createdAt] ON [POST] ([createdAt] DESC);
GO

CREATE INDEX [IX_POST_expiresAt] ON [POST] ([expiresAt]) WHERE ([expiresAt] IS NOT NULL);
GO

CREATE INDEX [IX_POST_groupId] ON [POST] ([groupId]) WHERE ([groupId] IS NOT NULL);
GO

CREATE INDEX [IX_POST_COMMENT_parent] ON [POST_COMMENT] ([parentCommentId]) WHERE ([parentCommentId] IS NOT NULL);
GO

CREATE INDEX [IX_POST_COMMENT_postId] ON [POST_COMMENT] ([postId], [createdAt]);
GO

CREATE INDEX [IX_POST_COMMENT_userId] ON [POST_COMMENT] ([userId]);
GO

CREATE INDEX [IX_POST_LIKE_postId] ON [POST_LIKE] ([postId]);
GO

CREATE INDEX [IX_POST_LIKE_userId] ON [POST_LIKE] ([userId]);
GO

CREATE UNIQUE INDEX [UQ_POST_LIKE_USER_POST] ON [POST_LIKE] ([postId], [userId]);
GO

CREATE INDEX [IX_POST_MEDIA_postId] ON [POST_MEDIA] ([postId], [displayOrder]);
GO

CREATE INDEX [IX_RATING_HISTORY_target] ON [RATING_HISTORY] ([targetId], [targetType]);
GO

CREATE INDEX [IX_RATING_HISTORY_userId] ON [RATING_HISTORY] ([userId]);
GO

CREATE UNIQUE INDEX [UQ_RATING_HISTORY_booking_user] ON [RATING_HISTORY] ([bookingId], [userId]) WHERE ([bookingId] IS NOT NULL);
GO

CREATE INDEX [IX_SCORECARD_courtId] ON [SCORECARD] ([courtId]);
GO

CREATE INDEX [IX_SCORECARD_matchId] ON [SCORECARD] ([matchId]);
GO

CREATE INDEX [IX_SKILL_MATCHUP_matchId] ON [SKILL_MATCHUP] ([matchId]);
GO

CREATE INDEX [IX_SKILL_MATCHUP_playerId] ON [SKILL_MATCHUP] ([playerId]);
GO

CREATE INDEX [IX_SOCIAL_GROUP_ownerId] ON [SOCIAL_GROUP] ([ownerId]);
GO

CREATE INDEX [IX_STAFF_userId] ON [STAFF] ([userId]);
GO

CREATE INDEX [IX_STAFF_venueId] ON [STAFF] ([venueId]);
GO

CREATE UNIQUE INDEX [UQ_STAFF_userId_venueId] ON [STAFF] ([userId], [venueId]);
GO

CREATE INDEX [IX_TEAM_captainId] ON [TEAM] ([captainId]);
GO

CREATE INDEX [IX_TOURNAMENT_TEAM_teamId] ON [TOURNAMENT_TEAM] ([teamId]);
GO

CREATE UNIQUE INDEX [UQ_USER_email] ON [USER] ([email]);
GO

CREATE UNIQUE INDEX [UQ_USER_username] ON [USER] ([username]);
GO

CREATE INDEX [IX_VENUE_ownerId] ON [VENUE] ([ownerId]);
GO

CREATE INDEX [IX_VENUE_AUDIT_LOG_actorId] ON [VENUE_AUDIT_LOG] ([actorId]);
GO

CREATE INDEX [IX_VENUE_AUDIT_venueId] ON [VENUE_AUDIT_LOG] ([venueId]);
GO

CREATE INDEX [IX_VENUE_IMAGE_venueId] ON [VENUE_IMAGE] ([venueId]);
GO

CREATE INDEX [IX_VENUE_OWNER_userId] ON [VENUE_OWNER] ([userId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260624155423_InitialCreate', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260625014753_AddGroupImageAndRatingAndRules', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TOURNAMENT]') AND [c].[name] = N'status');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [TOURNAMENT] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [TOURNAMENT] ADD DEFAULT N'Draft' FOR [status];
GO

ALTER TABLE [TOURNAMENT] ADD [address] nvarchar(500) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [approvedAt] datetime2 NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [approvedByUserId] int NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [bracketType] nvarchar(100) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [capacity] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [TOURNAMENT] ADD [city] nvarchar(100) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [createdAt] datetime2 NOT NULL DEFAULT ((getutcdate()));
GO

ALTER TABLE [TOURNAMENT] ADD [createdByUserId] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [TOURNAMENT] ADD [description] nvarchar(max) NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [entryFee] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [TOURNAMENT] ADD [format] nvarchar(100) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [imageUrl] nvarchar(1000) NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [organizerName] nvarchar(200) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [organizerPhone] nvarchar(30) NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [prizePool] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [TOURNAMENT] ADD [registrationDeadline] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
GO

ALTER TABLE [TOURNAMENT] ADD [resultsPublishedAt] datetime2 NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [rules] nvarchar(max) NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [skillLevel] nvarchar(100) NULL;
GO

ALTER TABLE [TOURNAMENT] ADD [slug] nvarchar(220) NOT NULL DEFAULT N'';
GO

ALTER TABLE [TOURNAMENT] ADD [updatedAt] datetime2 NOT NULL DEFAULT ((getutcdate()));
GO

ALTER TABLE [TOURNAMENT] ADD [venueName] nvarchar(200) NOT NULL DEFAULT N'';
GO

CREATE TABLE [TOURNAMENT_DIVISION] (
    [tournamentDivisionId] int NOT NULL IDENTITY,
    [tournamentId] int NOT NULL,
    [name] nvarchar(150) NOT NULL,
    [description] nvarchar(500) NULL,
    [skillLevel] nvarchar(100) NULL,
    [capacity] int NOT NULL,
    [entryFee] decimal(18,2) NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Open',
    [displayOrder] int NOT NULL,
    CONSTRAINT [PK_TOURNAMENT_DIVISION] PRIMARY KEY ([tournamentDivisionId]),
    CONSTRAINT [FK_TOURNAMENT_DIVISION_TOURNAMENT] FOREIGN KEY ([tournamentId]) REFERENCES [TOURNAMENT] ([tournamentId]) ON DELETE CASCADE
);
GO

CREATE TABLE [TOURNAMENT_REGISTRATION] (
    [tournamentRegistrationId] int NOT NULL IDENTITY,
    [tournamentId] int NOT NULL,
    [tournamentDivisionId] int NOT NULL,
    [captainPlayerId] int NOT NULL,
    [teamName] nvarchar(200) NOT NULL,
    [partnerName] nvarchar(200) NULL,
    [representativePhone] nvarchar(30) NOT NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Pending',
    [paymentStatus] nvarchar(30) NOT NULL DEFAULT N'Unpaid',
    [amountDue] decimal(18,2) NOT NULL,
    [registeredAt] datetime2 NOT NULL DEFAULT ((getutcdate())),
    [reviewedAt] datetime2 NULL,
    [reviewedByUserId] int NULL,
    [rejectionReason] nvarchar(500) NULL,
    [checkInCode] nvarchar(40) NULL,
    [checkedInAt] datetime2 NULL,
    [checkedInByUserId] int NULL,
    [seed] int NULL,
    CONSTRAINT [PK_TOURNAMENT_REGISTRATION] PRIMARY KEY ([tournamentRegistrationId]),
    CONSTRAINT [FK_TOURNAMENT_REGISTRATION_DIVISION] FOREIGN KEY ([tournamentDivisionId]) REFERENCES [TOURNAMENT_DIVISION] ([tournamentDivisionId]),
    CONSTRAINT [FK_TOURNAMENT_REGISTRATION_PLAYER] FOREIGN KEY ([captainPlayerId]) REFERENCES [PLAYER] ([playerId]),
    CONSTRAINT [FK_TOURNAMENT_REGISTRATION_TOURNAMENT] FOREIGN KEY ([tournamentId]) REFERENCES [TOURNAMENT] ([tournamentId])
);
GO

CREATE TABLE [TOURNAMENT_MATCH] (
    [tournamentMatchId] int NOT NULL IDENTITY,
    [tournamentId] int NOT NULL,
    [tournamentDivisionId] int NOT NULL,
    [roundName] nvarchar(100) NOT NULL,
    [matchNumber] int NOT NULL,
    [team1RegistrationId] int NULL,
    [team2RegistrationId] int NULL,
    [scheduledAt] datetime2 NULL,
    [courtName] nvarchar(100) NULL,
    [team1Score] int NULL,
    [team2Score] int NULL,
    [winnerRegistrationId] int NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Scheduled',
    [notes] nvarchar(1000) NULL,
    [createdAt] datetime2 NOT NULL DEFAULT ((getutcdate())),
    [updatedAt] datetime2 NOT NULL DEFAULT ((getutcdate())),
    CONSTRAINT [PK_TOURNAMENT_MATCH] PRIMARY KEY ([tournamentMatchId]),
    CONSTRAINT [FK_TOURNAMENT_MATCH_DIVISION] FOREIGN KEY ([tournamentDivisionId]) REFERENCES [TOURNAMENT_DIVISION] ([tournamentDivisionId]),
    CONSTRAINT [FK_TOURNAMENT_MATCH_TEAM1] FOREIGN KEY ([team1RegistrationId]) REFERENCES [TOURNAMENT_REGISTRATION] ([tournamentRegistrationId]),
    CONSTRAINT [FK_TOURNAMENT_MATCH_TEAM2] FOREIGN KEY ([team2RegistrationId]) REFERENCES [TOURNAMENT_REGISTRATION] ([tournamentRegistrationId]),
    CONSTRAINT [FK_TOURNAMENT_MATCH_TOURNAMENT] FOREIGN KEY ([tournamentId]) REFERENCES [TOURNAMENT] ([tournamentId]),
    CONSTRAINT [FK_TOURNAMENT_MATCH_WINNER] FOREIGN KEY ([winnerRegistrationId]) REFERENCES [TOURNAMENT_REGISTRATION] ([tournamentRegistrationId])
);
GO

CREATE TABLE [TOURNAMENT_PAYMENT] (
    [tournamentPaymentId] int NOT NULL IDENTITY,
    [tournamentRegistrationId] int NOT NULL,
    [amount] decimal(18,2) NOT NULL,
    [paymentMethod] nvarchar(50) NOT NULL,
    [transferContent] nvarchar(250) NULL,
    [receiptImageUrl] nvarchar(1000) NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Pending',
    [submittedAt] datetime2 NOT NULL DEFAULT ((getutcdate())),
    [verifiedAt] datetime2 NULL,
    [verifiedByUserId] int NULL,
    [rejectionReason] nvarchar(500) NULL,
    CONSTRAINT [PK_TOURNAMENT_PAYMENT] PRIMARY KEY ([tournamentPaymentId]),
    CONSTRAINT [FK_TOURNAMENT_PAYMENT_REGISTRATION] FOREIGN KEY ([tournamentRegistrationId]) REFERENCES [TOURNAMENT_REGISTRATION] ([tournamentRegistrationId]) ON DELETE CASCADE
);
GO

UPDATE [TOURNAMENT]
SET
    [slug] = CONCAT(N'tournament-', [tournamentId]),
    [venueName] = CASE WHEN [venueName] = N'' THEN [name] ELSE [venueName] END,
    [address] = CASE WHEN [address] = N'' THEN N'Chưa cập nhật' ELSE [address] END,
    [city] = CASE WHEN [city] = N'' THEN N'Chưa cập nhật' ELSE [city] END,
    [organizerName] = CASE WHEN [organizerName] = N'' THEN N'Picklink' ELSE [organizerName] END,
    [format] = CASE WHEN [format] = N'' THEN N'Chưa cấu hình' ELSE [format] END,
    [bracketType] = CASE WHEN [bracketType] = N'' THEN N'Nhập kết quả thủ công' ELSE [bracketType] END,
    [capacity] = CASE WHEN [capacity] = 0 THEN 32 ELSE [capacity] END,
    [registrationDeadline] = DATEADD(day, -1, CAST([startDate] AS datetime2)),
    [status] = CASE
        WHEN [status] = N'Upcoming' AND [startDate] >= CAST(GETUTCDATE() AS date) THEN N'Open'
        WHEN [status] = N'Upcoming' THEN N'Completed'
        ELSE [status]
    END
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_slug] ON [TOURNAMENT] ([slug]);
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_DIVISION_name] ON [TOURNAMENT_DIVISION] ([tournamentId], [name]);
GO

CREATE INDEX [IX_TOURNAMENT_MATCH_team1RegistrationId] ON [TOURNAMENT_MATCH] ([team1RegistrationId]);
GO

CREATE INDEX [IX_TOURNAMENT_MATCH_team2RegistrationId] ON [TOURNAMENT_MATCH] ([team2RegistrationId]);
GO

CREATE INDEX [IX_TOURNAMENT_MATCH_tournamentId] ON [TOURNAMENT_MATCH] ([tournamentId]);
GO

CREATE INDEX [IX_TOURNAMENT_MATCH_winnerRegistrationId] ON [TOURNAMENT_MATCH] ([winnerRegistrationId]);
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_MATCH_round] ON [TOURNAMENT_MATCH] ([tournamentDivisionId], [roundName], [matchNumber]);
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_PAYMENT_registration] ON [TOURNAMENT_PAYMENT] ([tournamentRegistrationId]);
GO

CREATE INDEX [IX_TOURNAMENT_REGISTRATION_captainPlayerId] ON [TOURNAMENT_REGISTRATION] ([captainPlayerId]);
GO

CREATE INDEX [IX_TOURNAMENT_REGISTRATION_tournamentDivisionId] ON [TOURNAMENT_REGISTRATION] ([tournamentDivisionId]);
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_REGISTRATION_captain] ON [TOURNAMENT_REGISTRATION] ([tournamentId], [captainPlayerId]);
GO

CREATE UNIQUE INDEX [UQ_TOURNAMENT_REGISTRATION_checkInCode] ON [TOURNAMENT_REGISTRATION] ([checkInCode]) WHERE [checkInCode] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260628111548_Phase10TournamentStep1', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'MESSAGE', N'isPinned') IS NULL
    ALTER TABLE [MESSAGE] ADD [isPinned] bit NOT NULL CONSTRAINT [DF_MESSAGE_isPinned] DEFAULT (0);
GO

IF COL_LENGTH(N'MATCH', N'availableDateFrom') IS NULL
    ALTER TABLE [MATCH] ADD [availableDateFrom] date NULL;
GO

IF COL_LENGTH(N'MATCH', N'availableDateTo') IS NULL
    ALTER TABLE [MATCH] ADD [availableDateTo] date NULL;
GO

IF COL_LENGTH(N'MATCH', N'maxSkillLevel') IS NULL
    ALTER TABLE [MATCH] ADD [maxSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_maxSkillLevel] DEFAULT (5);
GO

IF COL_LENGTH(N'MATCH', N'minSkillLevel') IS NULL
    ALTER TABLE [MATCH] ADD [minSkillLevel] int NOT NULL CONSTRAINT [DF_MATCH_minSkillLevel] DEFAULT (1);
GO

IF COL_LENGTH(N'MATCH', N'province') IS NULL
    ALTER TABLE [MATCH] ADD [province] nvarchar(100) NULL;
GO

IF COL_LENGTH(N'MATCH', N'searchLatitude') IS NULL
    ALTER TABLE [MATCH] ADD [searchLatitude] float NULL;
GO

IF COL_LENGTH(N'MATCH', N'searchLongitude') IS NULL
    ALTER TABLE [MATCH] ADD [searchLongitude] float NULL;
GO

IF COL_LENGTH(N'MATCH', N'searchRadiusKm') IS NULL
    ALTER TABLE [MATCH] ADD [searchRadiusKm] float NOT NULL CONSTRAINT [DF_MATCH_searchRadiusKm] DEFAULT (5);
GO

IF COL_LENGTH(N'MATCH', N'title') IS NULL
    ALTER TABLE [MATCH] ADD [title] nvarchar(200) NULL;
GO

IF COL_LENGTH(N'MATCH', N'ward') IS NULL
    ALTER TABLE [MATCH] ADD [ward] nvarchar(150) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260702032626_AddMessageIsPinned', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260703092751_AddMatchAvailabilitySlots', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[SOCIAL_GROUP]', N'U') IS NOT NULL
    AND COL_LENGTH(N'SOCIAL_GROUP', N'activeLocation') IS NULL
BEGIN
    ALTER TABLE [SOCIAL_GROUP] ADD [activeLocation] nvarchar(255) NULL;
END
GO

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
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260704175000_AddCommunityRuntimeSchemaMigration', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [PAYMENT] ADD [paymentGroupId] uniqueidentifier NULL;
GO

CREATE INDEX [IX_PAYMENT_paymentGroupId] ON [PAYMENT] ([paymentGroupId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260705002333_AddPaymentGroupId', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Provinces] (
    [Code] nvarchar(10) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [FullName] nvarchar(130) NOT NULL,
    CONSTRAINT [PK_Provinces] PRIMARY KEY ([Code])
);
GO

CREATE TABLE [Wards] (
    [Code] nvarchar(20) NOT NULL,
    [ProvinceCode] nvarchar(10) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [FullName] nvarchar(180) NOT NULL,
    CONSTRAINT [PK_Wards] PRIMARY KEY ([Code]),
    CONSTRAINT [FK_Wards_Provinces_ProvinceCode] FOREIGN KEY ([ProvinceCode]) REFERENCES [Provinces] ([Code])
);
GO

CREATE INDEX [IX_Wards_ProvinceCode] ON [Wards] ([ProvinceCode]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260708145501_AddAdministrativeLocations', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [MATCHMAKING_QUEUE] (
    [matchmakingQueueId] int NOT NULL IDENTITY,
    [playerId] int NULL,
    [matchId] int NULL,
    [matchType] nvarchar(100) NOT NULL,
    [skillLevel] int NOT NULL,
    [searchLatitude] float NULL,
    [searchLongitude] float NULL,
    [searchRadiusKm] float NOT NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    CONSTRAINT [PK_MATCHMAKING_QUEUE] PRIMARY KEY ([matchmakingQueueId]),
    CONSTRAINT [FK_MATCHMAKING_QUEUE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MATCHMAKING_QUEUE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]) ON DELETE CASCADE
);
GO

CREATE TABLE [MATCHMAKING_QUEUE_SLOT] (
    [matchmakingQueueSlotId] int NOT NULL IDENTITY,
    [matchmakingQueueId] int NOT NULL,
    [dayOfWeek] int NULL,
    [specificDate] date NULL,
    [dayOfMonth] int NULL,
    [timeStart] time NOT NULL,
    [timeEnd] time NOT NULL,
    CONSTRAINT [PK_MATCHMAKING_QUEUE_SLOT] PRIMARY KEY ([matchmakingQueueSlotId]),
    CONSTRAINT [FK_MATCHMAKING_QUEUE_SLOT_QUEUE] FOREIGN KEY ([matchmakingQueueId]) REFERENCES [MATCHMAKING_QUEUE] ([matchmakingQueueId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_MATCHMAKING_QUEUE_matchId] ON [MATCHMAKING_QUEUE] ([matchId]);
GO

CREATE INDEX [IX_MATCHMAKING_QUEUE_playerId] ON [MATCHMAKING_QUEUE] ([playerId]);
GO

CREATE INDEX [IX_MATCHMAKING_QUEUE_SLOT_matchmakingQueueId] ON [MATCHMAKING_QUEUE_SLOT] ([matchmakingQueueId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260709110822_AddMatchmakingQueue', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCH] ADD [replayType] nvarchar(50) NOT NULL DEFAULT N'None';
GO

ALTER TABLE [MATCH] ADD [replayWeekdays] nvarchar(100) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260709111317_AddMatchReplayFields', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [FK_MATCHMAKING_QUEUE_MATCH];
GO

ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [FK_MATCHMAKING_QUEUE_PLAYER];
GO

DROP INDEX [IX_MATCHMAKING_QUEUE_matchId] ON [MATCHMAKING_QUEUE];
GO

DROP INDEX [IX_MATCHMAKING_QUEUE_playerId] ON [MATCHMAKING_QUEUE];
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MATCHMAKING_QUEUE]') AND [c].[name] = N'matchId');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [MATCHMAKING_QUEUE] DROP COLUMN [matchId];
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MATCHMAKING_QUEUE]') AND [c].[name] = N'playerId');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [MATCHMAKING_QUEUE] DROP COLUMN [playerId];
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [isActive] bit NOT NULL DEFAULT CAST(1 AS bit);
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [replayType] nvarchar(50) NOT NULL DEFAULT N'None';
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [replayWeekdays] nvarchar(100) NULL;
GO

ALTER TABLE [CONVERSATION] ADD [matchmakingQueueId] int NULL;
GO

CREATE TABLE [MATCHMAKING_QUEUE_PLAYER] (
    [matchmakingQueuePlayerId] int NOT NULL IDENTITY,
    [matchmakingQueueId] int NOT NULL,
    [playerId] int NOT NULL,
    [isHost] bit NOT NULL,
    CONSTRAINT [PK_MATCHMAKING_QUEUE_PLAYER] PRIMARY KEY ([matchmakingQueuePlayerId]),
    CONSTRAINT [FK_MATCHMAKING_QUEUE_PLAYER_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MATCHMAKING_QUEUE_PLAYER_QUEUE] FOREIGN KEY ([matchmakingQueueId]) REFERENCES [MATCHMAKING_QUEUE] ([matchmakingQueueId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_CONVERSATION_matchmakingQueueId] ON [CONVERSATION] ([matchmakingQueueId]);
GO

CREATE UNIQUE INDEX [IX_MATCHMAKING_QUEUE_PLAYER_matchmakingQueueId_playerId] ON [MATCHMAKING_QUEUE_PLAYER] ([matchmakingQueueId], [playerId]);
GO

CREATE INDEX [IX_MATCHMAKING_QUEUE_PLAYER_playerId] ON [MATCHMAKING_QUEUE_PLAYER] ([playerId]);
GO

ALTER TABLE [CONVERSATION] ADD CONSTRAINT [FK_CONVERSATION_MATCHMAKING_QUEUE] FOREIGN KEY ([matchmakingQueueId]) REFERENCES [MATCHMAKING_QUEUE] ([matchmakingQueueId]) ON DELETE CASCADE;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260709120252_AddMatchmakingQueuePartyAndChat', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO


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

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260712171113_AddBookingSlotsAndCheckInGroups', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [isPublic] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [province] nvarchar(150) NULL;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [sharedVenues] nvarchar(500) NULL;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [updatedAt] datetime NOT NULL DEFAULT ((getutcdate()));
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [ward] nvarchar(150) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260713042355_AddMatchmakingQueueFilterFields', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BOOKING]') AND [c].[name] = N'hourlyPriceSnapshot');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [BOOKING] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [BOOKING] ALTER COLUMN [hourlyPriceSnapshot] decimal(18,2) NOT NULL;
GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BOOKING]') AND [c].[name] = N'courtAmount');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [BOOKING] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [BOOKING] ALTER COLUMN [courtAmount] decimal(18,2) NOT NULL;
GO

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BOOKING]') AND [c].[name] = N'totalAmount');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [BOOKING] DROP CONSTRAINT [' + @var5 + '];');
ALTER TABLE [BOOKING] ALTER COLUMN [totalAmount] decimal(18,2) NOT NULL;
GO

DECLARE @var6 sysname;
SELECT @var6 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BOOKING_SLOT]') AND [c].[name] = N'hourlyPriceSnapshot');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [BOOKING_SLOT] DROP CONSTRAINT [' + @var6 + '];');
ALTER TABLE [BOOKING_SLOT] ALTER COLUMN [hourlyPriceSnapshot] decimal(18,2) NOT NULL;
GO

DECLARE @var7 sysname;
SELECT @var7 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BOOKING_SLOT]') AND [c].[name] = N'courtAmount');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [BOOKING_SLOT] DROP CONSTRAINT [' + @var7 + '];');
ALTER TABLE [BOOKING_SLOT] ALTER COLUMN [courtAmount] decimal(18,2) NOT NULL;
GO

DECLARE @var8 sysname;
SELECT @var8 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[COURT]') AND [c].[name] = N'hourlyPrice');
IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [COURT] DROP CONSTRAINT [' + @var8 + '];');
ALTER TABLE [COURT] ALTER COLUMN [hourlyPrice] decimal(18,2) NOT NULL;
GO

DECLARE @var9 sysname;
SELECT @var9 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[INVENTORY_ITEM]') AND [c].[name] = N'pricePerUnit');
IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [INVENTORY_ITEM] DROP CONSTRAINT [' + @var9 + '];');
ALTER TABLE [INVENTORY_ITEM] ALTER COLUMN [pricePerUnit] decimal(18,2) NOT NULL;
GO

DECLARE @var10 sysname;
SELECT @var10 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PAYMENT]') AND [c].[name] = N'amount');
IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [PAYMENT] DROP CONSTRAINT [' + @var10 + '];');
ALTER TABLE [PAYMENT] ALTER COLUMN [amount] decimal(18,2) NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260716180839_ConvertMoneyToDecimal', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717090000_RepairMissingForeignKeyIndexes', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCHMAKING_QUEUE_PLAYER] ADD [status] nvarchar(20) NOT NULL DEFAULT N'Approved';
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717102705_AddMatchmakingQueuePlayerApproval', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717123000_RepairPersistedVietnameseText', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [title] nvarchar(150) NULL;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [playerCount] int NULL;
GO

UPDATE [MATCHMAKING_QUEUE]
SET [title] = N'Lời mời ghép trận',
    [playerCount] = CASE WHEN [matchType] = '1vs1' THEN 2 ELSE 4 END
WHERE [title] IS NULL OR [playerCount] IS NULL;
GO

DECLARE @var11 sysname;
SELECT @var11 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MATCHMAKING_QUEUE]') AND [c].[name] = N'title');
IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [' + @var11 + '];');
ALTER TABLE [MATCHMAKING_QUEUE] ALTER COLUMN [title] nvarchar(150) NOT NULL;
GO

DECLARE @var12 sysname;
SELECT @var12 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MATCHMAKING_QUEUE]') AND [c].[name] = N'playerCount');
IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [MATCHMAKING_QUEUE] DROP CONSTRAINT [' + @var12 + '];');
ALTER TABLE [MATCHMAKING_QUEUE] ALTER COLUMN [playerCount] int NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717130000_AddMatchmakingQueueTitleAndPlayerCount', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [minSkillLevel] int NOT NULL DEFAULT 1;
GO

ALTER TABLE [MATCHMAKING_QUEUE] ADD [maxSkillLevel] int NOT NULL DEFAULT 5;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717140000_AddMatchmakingQueueSkillRange', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [TICKET_SESSION] (
    [ticketSessionId] int NOT NULL IDENTITY,
    [bookingId] int NOT NULL,
    [title] nvarchar(200) NOT NULL,
    [description] nvarchar(2000) NULL,
    [skillLevel] nvarchar(50) NOT NULL,
    [playFormat] nvarchar(50) NOT NULL,
    [maxPlayers] int NOT NULL,
    [ticketPrice] decimal(18,2) NOT NULL,
    [cancellationDeadlineHours] int NOT NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'Draft',
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    [updatedAt] datetime NOT NULL DEFAULT ((getutcdate())),
    [publishedAt] datetime NULL,
    [cancelledAt] datetime NULL,
    [cancellationReason] nvarchar(500) NULL,
    CONSTRAINT [PK_TICKET_SESSION] PRIMARY KEY ([ticketSessionId]),
    CONSTRAINT [CK_TICKET_SESSION_cancel_hours] CHECK ([cancellationDeadlineHours] >= 0),
    CONSTRAINT [CK_TICKET_SESSION_capacity] CHECK ([maxPlayers] > 0),
    CONSTRAINT [CK_TICKET_SESSION_price] CHECK ([ticketPrice] >= 0),
    CONSTRAINT [CK_TICKET_SESSION_status] CHECK ([status] IN ('Draft','Published','Cancelled')),
    CONSTRAINT [FK_TICKET_SESSION_BOOKING] FOREIGN KEY ([bookingId]) REFERENCES [BOOKING] ([bookingId]) ON DELETE NO ACTION
);
GO

CREATE TABLE [SESSION_TICKET] (
    [sessionTicketId] int NOT NULL IDENTITY,
    [ticketSessionId] int NOT NULL,
    [playerId] int NOT NULL,
    [paymentId] int NOT NULL,
    [ticketCode] nvarchar(40) NOT NULL,
    [status] nvarchar(30) NOT NULL DEFAULT N'PendingPayment',
    [holdExpiresAt] datetime NULL,
    [createdAt] datetime NOT NULL DEFAULT ((getutcdate())),
    [cancelledAt] datetime NULL,
    [cancellationReason] nvarchar(500) NULL,
    [checkedInAt] datetime NULL,
    [checkedInByStaffId] int NULL,
    CONSTRAINT [PK_SESSION_TICKET] PRIMARY KEY ([sessionTicketId]),
    CONSTRAINT [CK_SESSION_TICKET_status] CHECK ([status] IN ('PendingPayment','Paid','CheckedIn','Cancelled','Expired','RefundPending','Refunded')),
    CONSTRAINT [FK_SESSION_TICKET_PAYMENT] FOREIGN KEY ([paymentId]) REFERENCES [PAYMENT] ([paymentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SESSION_TICKET_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SESSION_TICKET_SESSION] FOREIGN KEY ([ticketSessionId]) REFERENCES [TICKET_SESSION] ([ticketSessionId]) ON DELETE CASCADE,
    CONSTRAINT [FK_SESSION_TICKET_STAFF] FOREIGN KEY ([checkedInByStaffId]) REFERENCES [STAFF] ([staffId])
);
GO

CREATE INDEX [IX_SESSION_TICKET_checkedInByStaffId] ON [SESSION_TICKET] ([checkedInByStaffId]);
GO

CREATE INDEX [IX_SESSION_TICKET_player_createdAt] ON [SESSION_TICKET] ([playerId], [createdAt]);
GO

CREATE INDEX [IX_SESSION_TICKET_session_status_hold] ON [SESSION_TICKET] ([ticketSessionId], [status], [holdExpiresAt]);
GO

CREATE UNIQUE INDEX [UQ_SESSION_TICKET_code] ON [SESSION_TICKET] ([ticketCode]);
GO

CREATE UNIQUE INDEX [UQ_SESSION_TICKET_paymentId] ON [SESSION_TICKET] ([paymentId]);
GO

CREATE UNIQUE INDEX [UQ_SESSION_TICKET_session_player] ON [SESSION_TICKET] ([ticketSessionId], [playerId]);
GO

CREATE INDEX [IX_TICKET_SESSION_status_createdAt] ON [TICKET_SESSION] ([status], [createdAt]);
GO

CREATE UNIQUE INDEX [UQ_TICKET_SESSION_bookingId] ON [TICKET_SESSION] ([bookingId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260718055322_AddTicketSessions', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [SEPAY_TRANSACTION] (
    [sePayTransactionId] int NOT NULL IDENTITY,
    [externalTransactionId] bigint NOT NULL,
    [paymentId] int NOT NULL,
    [amount] decimal(18,2) NOT NULL,
    [accountNumber] nvarchar(100) NOT NULL,
    [referenceCode] nvarchar(200) NULL,
    [status] nvarchar(30) NOT NULL,
    [receivedAt] datetime NOT NULL,
    [refundedAt] datetime NULL,
    [refundReference] nvarchar(200) NULL,
    CONSTRAINT [PK_SEPAY_TRANSACTION] PRIMARY KEY ([sePayTransactionId]),
    CONSTRAINT [CK_SEPAY_TRANSACTION_status] CHECK ([status] IN ('Applied','TicketRefundPending','AdditionalRefundPending','Refunded','ReviewRequired')),
    CONSTRAINT [FK_SEPAY_TRANSACTION_PAYMENT] FOREIGN KEY ([paymentId]) REFERENCES [PAYMENT] ([paymentId]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_SEPAY_TRANSACTION_payment_status] ON [SEPAY_TRANSACTION] ([paymentId], [status], [receivedAt]);
GO

CREATE UNIQUE INDEX [UQ_SEPAY_TRANSACTION_externalId] ON [SEPAY_TRANSACTION] ([externalTransactionId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260718064712_AddSePayTransactionLedger', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [BOOKING] ADD [holdRemainingSeconds] int NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260718190940_AddBookingHoldRemainingSeconds', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

INSERT INTO [BOOKING_STATUS_HISTORY]
    ([bookingId], [fromStatus], [toStatus], [reason], [actorUserId], [changedAt])
SELECT
    booking.[bookingId],
    N'Holding',
    N'Confirmed',
    N'Thanh toán chuyển khoản đã được xác nhận',
    paid.[verifiedByUserId],
    COALESCE(paid.[verifiedAt], paid.[paidAt], booking.[createdAt])
FROM [BOOKING] AS booking
CROSS APPLY (
    SELECT TOP (1)
        payment.[verifiedByUserId],
        payment.[verifiedAt],
        payment.[paidAt]
    FROM [PAYMENT] AS payment
    WHERE payment.[bookingId] = booking.[bookingId]
        AND payment.[status] = N'Paid'
    ORDER BY COALESCE(payment.[verifiedAt], payment.[paidAt]) DESC, payment.[paymentId] DESC
) AS paid
WHERE booking.[playerId] IS NOT NULL
    AND booking.[matchId] IS NULL
    AND NOT EXISTS (
        SELECT 1
        FROM [BOOKING_STATUS_HISTORY] AS history
        WHERE history.[bookingId] = booking.[bookingId]
            AND history.[toStatus] = N'Confirmed'
    );
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260720030000_BackfillConfirmedBookingStatusHistory', N'8.0.28');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [MATCH_SLOT_ABSENCE] (
    [matchSlotAbsenceId] int NOT NULL IDENTITY,
    [matchId] int NOT NULL,
    [bookingCheckInGroupId] int NOT NULL,
    [unavailablePlayerId] int NOT NULL,
    [status] nvarchar(20) NOT NULL DEFAULT N'Open',
    [reason] nvarchar(500) NULL,
    [createdAt] datetime NOT NULL,
    [updatedAt] datetime NOT NULL,
    CONSTRAINT [PK_MATCH_SLOT_ABSENCE] PRIMARY KEY ([matchSlotAbsenceId]),
    CONSTRAINT [FK_MATCH_SLOT_ABSENCE_GROUP] FOREIGN KEY ([bookingCheckInGroupId]) REFERENCES [BOOKING_CHECKIN_GROUP] ([bookingCheckInGroupId]),
    CONSTRAINT [FK_MATCH_SLOT_ABSENCE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH] ([matchId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MATCH_SLOT_ABSENCE_PLAYER] FOREIGN KEY ([unavailablePlayerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE TABLE [MATCH_SLOT_REPLACEMENT_REQUEST] (
    [matchSlotReplacementRequestId] int NOT NULL IDENTITY,
    [matchSlotAbsenceId] int NOT NULL,
    [playerId] int NOT NULL,
    [status] nvarchar(20) NOT NULL DEFAULT N'Pending',
    [requestedAt] datetime NOT NULL,
    [respondedAt] datetime NULL,
    CONSTRAINT [PK_MATCH_SLOT_REPLACEMENT_REQUEST] PRIMARY KEY ([matchSlotReplacementRequestId]),
    CONSTRAINT [FK_MATCH_SLOT_REPLACEMENT_ABSENCE] FOREIGN KEY ([matchSlotAbsenceId]) REFERENCES [MATCH_SLOT_ABSENCE] ([matchSlotAbsenceId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MATCH_SLOT_REPLACEMENT_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER] ([playerId])
);
GO

CREATE INDEX [IX_MATCH_SLOT_ABSENCE_group] ON [MATCH_SLOT_ABSENCE] ([bookingCheckInGroupId]);
GO

CREATE INDEX [IX_MATCH_SLOT_ABSENCE_match] ON [MATCH_SLOT_ABSENCE] ([matchId]);
GO

CREATE INDEX [IX_MATCH_SLOT_ABSENCE_player] ON [MATCH_SLOT_ABSENCE] ([unavailablePlayerId]);
GO

CREATE UNIQUE INDEX [UQ_MATCH_SLOT_ABSENCE_group_player] ON [MATCH_SLOT_ABSENCE] ([bookingCheckInGroupId], [unavailablePlayerId]);
GO

CREATE INDEX [IX_MATCH_SLOT_REPLACEMENT_absence] ON [MATCH_SLOT_REPLACEMENT_REQUEST] ([matchSlotAbsenceId]);
GO

CREATE INDEX [IX_MATCH_SLOT_REPLACEMENT_player] ON [MATCH_SLOT_REPLACEMENT_REQUEST] ([playerId]);
GO

CREATE UNIQUE INDEX [UQ_MATCH_SLOT_REPLACEMENT_absence_player] ON [MATCH_SLOT_REPLACEMENT_REQUEST] ([matchSlotAbsenceId], [playerId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260721092115_AddMatchSlotReplacements', N'8.0.28');
GO

COMMIT;
GO

