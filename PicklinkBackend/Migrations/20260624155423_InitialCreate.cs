using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TOURNAMENT",
                columns: table => new
                {
                    tournamentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    startDate = table.Column<DateOnly>(type: "date", nullable: false),
                    endDate = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Upcoming")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT", x => x.tournamentId);
                });

            migrationBuilder.CreateTable(
                name: "USER",
                columns: table => new
                {
                    userId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    passwordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    userType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    profileImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    commune = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER", x => x.userId);
                });

            migrationBuilder.CreateTable(
                name: "FRIENDSHIP",
                columns: table => new
                {
                    friendshipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    requesterId = table.Column<int>(type: "int", nullable: false),
                    receiverId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FRIENDSHIP", x => x.friendshipId);
                    table.ForeignKey(
                        name: "FK_FRIENDSHIP_RECEIVER",
                        column: x => x.receiverId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_FRIENDSHIP_REQUESTER",
                        column: x => x.requesterId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "MARKETPLACE_PROVIDER",
                columns: table => new
                {
                    providerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    specialty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    providerType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKETPLACE_PROVIDER", x => x.providerId);
                    table.ForeignKey(
                        name: "FK_MARKETPLACE_PROVIDER_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION_LOG",
                columns: table => new
                {
                    notifId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    isRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_LOG", x => x.notifId);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_LOG_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "PASSWORD_RESET_TOKEN",
                columns: table => new
                {
                    resetTokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    tokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    expiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    usedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PASSWORD_RESET_TOKEN", x => x.resetTokenId);
                    table.ForeignKey(
                        name: "FK_PASSWORD_RESET_TOKEN_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "PLAYER",
                columns: table => new
                {
                    playerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    prestige = table.Column<int>(type: "int", nullable: false),
                    skillLevel = table.Column<double>(type: "float", nullable: false),
                    playerSubType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    playFrequency = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    preferredTimeSlot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    bio = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    birthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    heightCm = table.Column<double>(type: "float", nullable: true),
                    weightKg = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PLAYER", x => x.playerId);
                    table.ForeignKey(
                        name: "FK_PLAYER_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "VENUE_OWNER",
                columns: table => new
                {
                    ownerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    specialPermissions = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE_OWNER", x => x.ownerId);
                    table.ForeignKey(
                        name: "FK_VENUE_OWNER_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "INVENTORY_ITEM",
                columns: table => new
                {
                    itemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    providerId = table.Column<int>(type: "int", nullable: false),
                    itemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    pricePerUnit = table.Column<double>(type: "float", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Available")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_ITEM", x => x.itemId);
                    table.ForeignKey(
                        name: "FK_INVENTORY_ITEM_PROVIDER",
                        column: x => x.providerId,
                        principalTable: "MARKETPLACE_PROVIDER",
                        principalColumn: "providerId");
                });

            migrationBuilder.CreateTable(
                name: "SOCIAL_GROUP",
                columns: table => new
                {
                    groupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ownerId = table.Column<int>(type: "int", nullable: false),
                    groupName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    groupType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Public"),
                    coverImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    rules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    overallRating = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    ratingCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SOCIAL_GROUP", x => x.groupId);
                    table.ForeignKey(
                        name: "FK_SOCIAL_GROUP_OWNER",
                        column: x => x.ownerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "TEAM",
                columns: table => new
                {
                    teamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    teamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    captainId = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TEAM", x => x.teamId);
                    table.ForeignKey(
                        name: "FK_TEAM_CAPTAIN",
                        column: x => x.captainId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "OWNER_BANK_ACCOUNT",
                columns: table => new
                {
                    ownerBankAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ownerId = table.Column<int>(type: "int", nullable: false),
                    bankCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    bankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    accountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    accountHolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OWNER_BANK_ACCOUNT", x => x.ownerBankAccountId);
                    table.ForeignKey(
                        name: "FK_OWNER_BANK_ACCOUNT_OWNER",
                        column: x => x.ownerId,
                        principalTable: "VENUE_OWNER",
                        principalColumn: "ownerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VENUE",
                columns: table => new
                {
                    venueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ownerId = table.Column<int>(type: "int", nullable: false),
                    venueName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    overallRating = table.Column<double>(type: "float", nullable: false),
                    openTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    closeTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    phoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    latitude = table.Column<double>(type: "float", nullable: true),
                    longitude = table.Column<double>(type: "float", nullable: true),
                    isOpen = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    approvalStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE", x => x.venueId);
                    table.ForeignKey(
                        name: "FK_VENUE_OWNER",
                        column: x => x.ownerId,
                        principalTable: "VENUE_OWNER",
                        principalColumn: "ownerId");
                });

            migrationBuilder.CreateTable(
                name: "GROUP_IMAGE",
                columns: table => new
                {
                    groupImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    groupId = table.Column<int>(type: "int", nullable: false),
                    imageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    sortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GROUP_IMAGE", x => x.groupImageId);
                    table.ForeignKey(
                        name: "FK_GROUP_IMAGE_GROUP",
                        column: x => x.groupId,
                        principalTable: "SOCIAL_GROUP",
                        principalColumn: "groupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GROUP_MEMBER",
                columns: table => new
                {
                    groupId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Member"),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Accepted"),
                    joinedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GROUP_MEMBER", x => new { x.groupId, x.userId });
                    table.ForeignKey(
                        name: "FK_GROUP_MEMBER_GROUP",
                        column: x => x.groupId,
                        principalTable: "SOCIAL_GROUP",
                        principalColumn: "groupId");
                    table.ForeignKey(
                        name: "FK_GROUP_MEMBER_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "POST",
                columns: table => new
                {
                    postId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    authorId = table.Column<int>(type: "int", nullable: false),
                    groupId = table.Column<int>(type: "int", nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    postType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Post"),
                    visibility = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Public"),
                    expiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POST", x => x.postId);
                    table.ForeignKey(
                        name: "FK_POST_AUTHOR",
                        column: x => x.authorId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_POST_SOCIAL_GROUP",
                        column: x => x.groupId,
                        principalTable: "SOCIAL_GROUP",
                        principalColumn: "groupId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH",
                columns: table => new
                {
                    matchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    hostPlayerId = table.Column<int>(type: "int", nullable: true),
                    matchType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    matchSkillLevel = table.Column<int>(type: "int", nullable: false),
                    requiredPlayerCount = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    matchTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Scheduled"),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    cancelledAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    preferredTimeStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    preferredTimeEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    sharedVenues = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    team1Id = table.Column<int>(type: "int", nullable: true),
                    team2Id = table.Column<int>(type: "int", nullable: true),
                    winningTeamId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH", x => x.matchId);
                    table.ForeignKey(
                        name: "FK_MATCH_HOST_PLAYER",
                        column: x => x.hostPlayerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_MATCH_TEAM1",
                        column: x => x.team1Id,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                    table.ForeignKey(
                        name: "FK_MATCH_TEAM2",
                        column: x => x.team2Id,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                    table.ForeignKey(
                        name: "FK_MATCH_WINNER",
                        column: x => x.winningTeamId,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                });

            migrationBuilder.CreateTable(
                name: "PLAYER_TEAM_ROSTER",
                columns: table => new
                {
                    playerId = table.Column<int>(type: "int", nullable: false),
                    teamId = table.Column<int>(type: "int", nullable: false),
                    joinedDate = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "(CONVERT([date],getdate()))")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PLAYER_TEAM_ROSTER", x => new { x.playerId, x.teamId });
                    table.ForeignKey(
                        name: "FK_PTR_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_PTR_TEAM",
                        column: x => x.teamId,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_TEAM",
                columns: table => new
                {
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    teamId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_TEAM", x => new { x.tournamentId, x.teamId });
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_TEAM_TEAM",
                        column: x => x.teamId,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_TEAM_TOURN",
                        column: x => x.tournamentId,
                        principalTable: "TOURNAMENT",
                        principalColumn: "tournamentId");
                });

            migrationBuilder.CreateTable(
                name: "AMENITY",
                columns: table => new
                {
                    amenityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    amenityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    isFree = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AMENITY", x => x.amenityId);
                    table.ForeignKey(
                        name: "FK_AMENITY_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId");
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_RULES",
                columns: table => new
                {
                    ruleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    ruleType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ruleContent = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_RULES", x => x.ruleId);
                    table.ForeignKey(
                        name: "FK_BOOKING_RULES_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId");
                });

            migrationBuilder.CreateTable(
                name: "COURT",
                columns: table => new
                {
                    courtId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    courtNumber = table.Column<int>(type: "int", nullable: false),
                    surfaceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    courtType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, defaultValue: "Standard"),
                    hourlyPrice = table.Column<double>(type: "float", nullable: false),
                    isIndoor = table.Column<bool>(type: "bit", nullable: false),
                    availabilityStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Available")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURT", x => x.courtId);
                    table.ForeignKey(
                        name: "FK_COURT_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId");
                });

            migrationBuilder.CreateTable(
                name: "FAVORITE_VENUE",
                columns: table => new
                {
                    playerId = table.Column<int>(type: "int", nullable: false),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAVORITE_VENUE", x => new { x.playerId, x.venueId });
                    table.ForeignKey(
                        name: "FK_FAVORITE_VENUE_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FAVORITE_VENUE_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "STAFF",
                columns: table => new
                {
                    staffId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    assignedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    assignedByUserId = table.Column<int>(type: "int", nullable: true),
                    revokedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STAFF", x => x.staffId);
                    table.ForeignKey(
                        name: "FK_STAFF_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_STAFF_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId");
                });

            migrationBuilder.CreateTable(
                name: "VENUE_AUDIT_LOG",
                columns: table => new
                {
                    logId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    actorId = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE_AUDIT_LOG", x => x.logId);
                    table.ForeignKey(
                        name: "FK_VENUE_AUDIT_LOG_ACTOR",
                        column: x => x.actorId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_VENUE_AUDIT_LOG_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId");
                });

            migrationBuilder.CreateTable(
                name: "VENUE_IMAGE",
                columns: table => new
                {
                    venueImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    imageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    isPrimary = table.Column<bool>(type: "bit", nullable: false),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE_IMAGE", x => x.venueImageId);
                    table.ForeignKey(
                        name: "FK_VENUE_IMAGE_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "POST_COMMENT",
                columns: table => new
                {
                    commentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    postId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    parentCommentId = table.Column<int>(type: "int", nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POST_COMMENT", x => x.commentId);
                    table.ForeignKey(
                        name: "FK_POST_COMMENT_PARENT",
                        column: x => x.parentCommentId,
                        principalTable: "POST_COMMENT",
                        principalColumn: "commentId");
                    table.ForeignKey(
                        name: "FK_POST_COMMENT_POST",
                        column: x => x.postId,
                        principalTable: "POST",
                        principalColumn: "postId");
                    table.ForeignKey(
                        name: "FK_POST_COMMENT_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "POST_LIKE",
                columns: table => new
                {
                    likeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    postId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    reactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Like"),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POST_LIKE", x => x.likeId);
                    table.ForeignKey(
                        name: "FK_POST_LIKE_POST",
                        column: x => x.postId,
                        principalTable: "POST",
                        principalColumn: "postId");
                    table.ForeignKey(
                        name: "FK_POST_LIKE_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "POST_MEDIA",
                columns: table => new
                {
                    mediaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    postId = table.Column<int>(type: "int", nullable: false),
                    mediaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    mediaType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Image"),
                    displayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POST_MEDIA", x => x.mediaId);
                    table.ForeignKey(
                        name: "FK_POST_MEDIA_POST",
                        column: x => x.postId,
                        principalTable: "POST",
                        principalColumn: "postId");
                });

            migrationBuilder.CreateTable(
                name: "CONVERSATION",
                columns: table => new
                {
                    conversationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    groupId = table.Column<int>(type: "int", nullable: true),
                    matchId = table.Column<int>(type: "int", nullable: true),
                    conversationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Direct"),
                    conversationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    lastMessageAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONVERSATION", x => x.conversationId);
                    table.ForeignKey(
                        name: "FK_CONVERSATION_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_CONVERSATION_SOCIAL_GROUP",
                        column: x => x.groupId,
                        principalTable: "SOCIAL_GROUP",
                        principalColumn: "groupId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH_PARTICIPANT",
                columns: table => new
                {
                    participantId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    @class = table.Column<string>(name: "class", type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Accepted"),
                    isHost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    requestedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    respondedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    votedVenueId = table.Column<int>(type: "int", nullable: true),
                    votedStartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    votedEndTime = table.Column<TimeOnly>(type: "time", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_PARTICIPANT", x => x.participantId);
                    table.ForeignKey(
                        name: "FK_MATCH_PARTICIPANT_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_MATCH_PARTICIPANT_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH_PLAYER_REVIEW",
                columns: table => new
                {
                    matchPlayerReviewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    reviewerPlayerId = table.Column<int>(type: "int", nullable: false),
                    revieweePlayerId = table.Column<int>(type: "int", nullable: false),
                    score = table.Column<int>(type: "int", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_PLAYER_REVIEW", x => x.matchPlayerReviewId);
                    table.ForeignKey(
                        name: "FK_MATCH_PLAYER_REVIEW_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCH_PLAYER_REVIEW_REVIEWEE",
                        column: x => x.revieweePlayerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_MATCH_PLAYER_REVIEW_REVIEWER",
                        column: x => x.reviewerPlayerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "SKILL_MATCHUP",
                columns: table => new
                {
                    matchupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    skillDelta = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SKILL_MATCHUP", x => x.matchupId);
                    table.ForeignKey(
                        name: "FK_SKILL_MATCHUP_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_SKILL_MATCHUP_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "BOOKING",
                columns: table => new
                {
                    bookingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    playerId = table.Column<int>(type: "int", nullable: true),
                    courtId = table.Column<int>(type: "int", nullable: false),
                    matchId = table.Column<int>(type: "int", nullable: true),
                    startTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    endTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    ownerEntryType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    bookingCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    holdExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    hourlyPriceSnapshot = table.Column<double>(type: "float", nullable: false),
                    courtAmount = table.Column<double>(type: "float", nullable: false),
                    totalAmount = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING", x => x.bookingId);
                    table.ForeignKey(
                        name: "FK_BOOKING_COURT",
                        column: x => x.courtId,
                        principalTable: "COURT",
                        principalColumn: "courtId");
                    table.ForeignKey(
                        name: "FK_BOOKING_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_BOOKING_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "SCORECARD",
                columns: table => new
                {
                    gameId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    courtId = table.Column<int>(type: "int", nullable: false),
                    scoreInfo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SCORECARD", x => x.gameId);
                    table.ForeignKey(
                        name: "FK_SCORECARD_COURT",
                        column: x => x.courtId,
                        principalTable: "COURT",
                        principalColumn: "courtId");
                    table.ForeignKey(
                        name: "FK_SCORECARD_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH_CHECKIN",
                columns: table => new
                {
                    checkinId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    staffId = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Present"),
                    checkedInAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_CHECKIN", x => x.checkinId);
                    table.ForeignKey(
                        name: "FK_MATCH_CHECKIN_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_MATCH_CHECKIN_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_MATCH_CHECKIN_STAFF",
                        column: x => x.staffId,
                        principalTable: "STAFF",
                        principalColumn: "staffId");
                });

            migrationBuilder.CreateTable(
                name: "CONVERSATION_PARTICIPANT",
                columns: table => new
                {
                    conversationId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    joinedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    lastReadAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONVERSATION_PARTICIPANT", x => new { x.conversationId, x.userId });
                    table.ForeignKey(
                        name: "FK_CONV_PARTICIPANT_CONVERSATION",
                        column: x => x.conversationId,
                        principalTable: "CONVERSATION",
                        principalColumn: "conversationId");
                    table.ForeignKey(
                        name: "FK_CONV_PARTICIPANT_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "MESSAGE",
                columns: table => new
                {
                    messageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    conversationId = table.Column<int>(type: "int", nullable: false),
                    senderId = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    messageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Text"),
                    mediaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    replyToMessageId = table.Column<int>(type: "int", nullable: true),
                    sentAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    isDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MESSAGE", x => x.messageId);
                    table.ForeignKey(
                        name: "FK_MESSAGE_CONVERSATION",
                        column: x => x.conversationId,
                        principalTable: "CONVERSATION",
                        principalColumn: "conversationId");
                    table.ForeignKey(
                        name: "FK_MESSAGE_REPLY",
                        column: x => x.replyToMessageId,
                        principalTable: "MESSAGE",
                        principalColumn: "messageId");
                    table.ForeignKey(
                        name: "FK_MESSAGE_SENDER",
                        column: x => x.senderId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_OPERATION",
                columns: table => new
                {
                    bookingOperationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    checkInStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Ready"),
                    codeVerifiedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    codeVerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    paymentConfirmedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    paymentConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    checkedInAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    checkedInByUserId = table.Column<int>(type: "int", nullable: true),
                    noShowAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    noShowByUserId = table.Column<int>(type: "int", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_OPERATION", x => x.bookingOperationId);
                    table.ForeignKey(
                        name: "FK_BOOKING_OPERATION_BOOKING",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_STATUS_HISTORY",
                columns: table => new
                {
                    bookingStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    fromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    toStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    actorUserId = table.Column<int>(type: "int", nullable: true),
                    changedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_STATUS_HISTORY", x => x.bookingStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_BOOKING_STATUS_HISTORY_BOOKING",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT",
                columns: table => new
                {
                    paymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    payerId = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<double>(type: "float", nullable: false),
                    paymentMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    paidAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    transferCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    transferContent = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    bankCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    bankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    bankAccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    bankAccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    qrImageUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    receiptImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    submittedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    verifiedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    verifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT", x => x.paymentId);
                    table.ForeignKey(
                        name: "FK_PAYMENT_BOOKING",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId");
                    table.ForeignKey(
                        name: "FK_PAYMENT_PAYER",
                        column: x => x.payerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "RATING_HISTORY",
                columns: table => new
                {
                    ratingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    bookingId = table.Column<int>(type: "int", nullable: true),
                    targetId = table.Column<int>(type: "int", nullable: false),
                    targetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    score = table.Column<int>(type: "int", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    isAnonymous = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RATING_HISTORY", x => x.ratingId);
                    table.ForeignKey(
                        name: "FK_RATING_HISTORY_BOOKING",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RATING_HISTORY_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_STATUS_HISTORY",
                columns: table => new
                {
                    paymentStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    paymentId = table.Column<int>(type: "int", nullable: false),
                    fromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    toStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    actorUserId = table.Column<int>(type: "int", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_STATUS_HISTORY", x => x.paymentStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_PAYMENT_STATUS_HISTORY_PAYMENT",
                        column: x => x.paymentId,
                        principalTable: "PAYMENT",
                        principalColumn: "paymentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AMENITY_venueId",
                table: "AMENITY",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_courtId",
                table: "BOOKING",
                column: "courtId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_matchId",
                table: "BOOKING",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_playerId",
                table: "BOOKING",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_startTime",
                table: "BOOKING",
                column: "startTime");

            migrationBuilder.CreateIndex(
                name: "UQ_BOOKING_OPERATION_bookingId",
                table: "BOOKING_OPERATION",
                column: "bookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_RULES_venueId",
                table: "BOOKING_RULES",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_STATUS_HISTORY_bookingId",
                table: "BOOKING_STATUS_HISTORY",
                column: "bookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_groupId",
                table: "CONVERSATION",
                column: "groupId",
                filter: "([groupId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_lastMessageAt",
                table: "CONVERSATION",
                column: "lastMessageAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_matchId",
                table: "CONVERSATION",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_CONV_PARTICIPANT_userId",
                table: "CONVERSATION_PARTICIPANT",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_COURT_venueId",
                table: "COURT",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_FAVORITE_VENUE_venueId",
                table: "FAVORITE_VENUE",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_FRIENDSHIP_receiver",
                table: "FRIENDSHIP",
                columns: new[] { "receiverId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_FRIENDSHIP_requester",
                table: "FRIENDSHIP",
                columns: new[] { "requesterId", "status" });

            migrationBuilder.CreateIndex(
                name: "UQ_FRIENDSHIP_PAIR",
                table: "FRIENDSHIP",
                columns: new[] { "requesterId", "receiverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GROUP_IMAGE_groupId",
                table: "GROUP_IMAGE",
                columns: new[] { "groupId", "sortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GROUP_MEMBER_userId",
                table: "GROUP_MEMBER",
                columns: new[] { "userId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_ITEM_providerId",
                table: "INVENTORY_ITEM",
                column: "providerId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKETPLACE_PROVIDER_userId",
                table: "MARKETPLACE_PROVIDER",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_hostPlayerId",
                table: "MATCH",
                column: "hostPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_matchTime",
                table: "MATCH",
                column: "matchTime");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_status",
                table: "MATCH",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_team1Id",
                table: "MATCH",
                column: "team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_team2Id",
                table: "MATCH",
                column: "team2Id");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_winningTeamId",
                table: "MATCH",
                column: "winningTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_CHECKIN_matchId",
                table: "MATCH_CHECKIN",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_CHECKIN_playerId",
                table: "MATCH_CHECKIN",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_CHECKIN_staffId",
                table: "MATCH_CHECKIN",
                column: "staffId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_CHECKIN_UNIQUE",
                table: "MATCH_CHECKIN",
                columns: new[] { "matchId", "playerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_PARTICIPANT_match",
                table: "MATCH_PARTICIPANT",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_PARTICIPANT_player",
                table: "MATCH_PARTICIPANT",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_PARTICIPANT_match_player",
                table: "MATCH_PARTICIPANT",
                columns: new[] { "matchId", "playerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_PLAYER_REVIEW_revieweePlayerId",
                table: "MATCH_PLAYER_REVIEW",
                column: "revieweePlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_PLAYER_REVIEW_reviewerPlayerId",
                table: "MATCH_PLAYER_REVIEW",
                column: "reviewerPlayerId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_PLAYER_REVIEW",
                table: "MATCH_PLAYER_REVIEW",
                columns: new[] { "matchId", "reviewerPlayerId", "revieweePlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_conversationId",
                table: "MESSAGE",
                columns: new[] { "conversationId", "sentAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_replyToMessageId",
                table: "MESSAGE",
                column: "replyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_senderId",
                table: "MESSAGE",
                column: "senderId");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIF_userId",
                table: "NOTIFICATION_LOG",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "UQ_OWNER_BANK_ACCOUNT_ownerId",
                table: "OWNER_BANK_ACCOUNT",
                column: "ownerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PASSWORD_RESET_TOKEN_tokenHash",
                table: "PASSWORD_RESET_TOKEN",
                column: "tokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_PASSWORD_RESET_TOKEN_userId",
                table: "PASSWORD_RESET_TOKEN",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_bookingId",
                table: "PAYMENT",
                column: "bookingId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_payerId",
                table: "PAYMENT",
                column: "payerId");

            migrationBuilder.CreateIndex(
                name: "UQ_PAYMENT_transferCode",
                table: "PAYMENT",
                column: "transferCode",
                unique: true,
                filter: "[transferCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_STATUS_HISTORY_paymentId",
                table: "PAYMENT_STATUS_HISTORY",
                column: "paymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PLAYER_userId",
                table: "PLAYER",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_PLAYER_TEAM_ROSTER_teamId",
                table: "PLAYER_TEAM_ROSTER",
                column: "teamId");

            migrationBuilder.CreateIndex(
                name: "IX_POST_authorId",
                table: "POST",
                column: "authorId");

            migrationBuilder.CreateIndex(
                name: "IX_POST_createdAt",
                table: "POST",
                column: "createdAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_POST_expiresAt",
                table: "POST",
                column: "expiresAt",
                filter: "([expiresAt] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_POST_groupId",
                table: "POST",
                column: "groupId",
                filter: "([groupId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_POST_COMMENT_parent",
                table: "POST_COMMENT",
                column: "parentCommentId",
                filter: "([parentCommentId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_POST_COMMENT_postId",
                table: "POST_COMMENT",
                columns: new[] { "postId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_POST_COMMENT_userId",
                table: "POST_COMMENT",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_POST_LIKE_postId",
                table: "POST_LIKE",
                column: "postId");

            migrationBuilder.CreateIndex(
                name: "IX_POST_LIKE_userId",
                table: "POST_LIKE",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "UQ_POST_LIKE_USER_POST",
                table: "POST_LIKE",
                columns: new[] { "postId", "userId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POST_MEDIA_postId",
                table: "POST_MEDIA",
                columns: new[] { "postId", "displayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RATING_HISTORY_target",
                table: "RATING_HISTORY",
                columns: new[] { "targetId", "targetType" });

            migrationBuilder.CreateIndex(
                name: "IX_RATING_HISTORY_userId",
                table: "RATING_HISTORY",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "UQ_RATING_HISTORY_booking_user",
                table: "RATING_HISTORY",
                columns: new[] { "bookingId", "userId" },
                unique: true,
                filter: "([bookingId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SCORECARD_courtId",
                table: "SCORECARD",
                column: "courtId");

            migrationBuilder.CreateIndex(
                name: "IX_SCORECARD_matchId",
                table: "SCORECARD",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_MATCHUP_matchId",
                table: "SKILL_MATCHUP",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_MATCHUP_playerId",
                table: "SKILL_MATCHUP",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "IX_SOCIAL_GROUP_ownerId",
                table: "SOCIAL_GROUP",
                column: "ownerId");

            migrationBuilder.CreateIndex(
                name: "IX_STAFF_userId",
                table: "STAFF",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_STAFF_venueId",
                table: "STAFF",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "UQ_STAFF_userId_venueId",
                table: "STAFF",
                columns: new[] { "userId", "venueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TEAM_captainId",
                table: "TEAM",
                column: "captainId");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_TEAM_teamId",
                table: "TOURNAMENT_TEAM",
                column: "teamId");

            migrationBuilder.CreateIndex(
                name: "UQ_USER_email",
                table: "USER",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_USER_username",
                table: "USER",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_ownerId",
                table: "VENUE",
                column: "ownerId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_AUDIT_LOG_actorId",
                table: "VENUE_AUDIT_LOG",
                column: "actorId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_AUDIT_venueId",
                table: "VENUE_AUDIT_LOG",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_IMAGE_venueId",
                table: "VENUE_IMAGE",
                column: "venueId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_OWNER_userId",
                table: "VENUE_OWNER",
                column: "userId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AMENITY");

            migrationBuilder.DropTable(
                name: "BOOKING_OPERATION");

            migrationBuilder.DropTable(
                name: "BOOKING_RULES");

            migrationBuilder.DropTable(
                name: "BOOKING_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "CONVERSATION_PARTICIPANT");

            migrationBuilder.DropTable(
                name: "FAVORITE_VENUE");

            migrationBuilder.DropTable(
                name: "FRIENDSHIP");

            migrationBuilder.DropTable(
                name: "GROUP_IMAGE");

            migrationBuilder.DropTable(
                name: "GROUP_MEMBER");

            migrationBuilder.DropTable(
                name: "INVENTORY_ITEM");

            migrationBuilder.DropTable(
                name: "MATCH_CHECKIN");

            migrationBuilder.DropTable(
                name: "MATCH_PARTICIPANT");

            migrationBuilder.DropTable(
                name: "MATCH_PLAYER_REVIEW");

            migrationBuilder.DropTable(
                name: "MESSAGE");

            migrationBuilder.DropTable(
                name: "NOTIFICATION_LOG");

            migrationBuilder.DropTable(
                name: "OWNER_BANK_ACCOUNT");

            migrationBuilder.DropTable(
                name: "PASSWORD_RESET_TOKEN");

            migrationBuilder.DropTable(
                name: "PAYMENT_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "PLAYER_TEAM_ROSTER");

            migrationBuilder.DropTable(
                name: "POST_COMMENT");

            migrationBuilder.DropTable(
                name: "POST_LIKE");

            migrationBuilder.DropTable(
                name: "POST_MEDIA");

            migrationBuilder.DropTable(
                name: "RATING_HISTORY");

            migrationBuilder.DropTable(
                name: "SCORECARD");

            migrationBuilder.DropTable(
                name: "SKILL_MATCHUP");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_TEAM");

            migrationBuilder.DropTable(
                name: "VENUE_AUDIT_LOG");

            migrationBuilder.DropTable(
                name: "VENUE_IMAGE");

            migrationBuilder.DropTable(
                name: "MARKETPLACE_PROVIDER");

            migrationBuilder.DropTable(
                name: "STAFF");

            migrationBuilder.DropTable(
                name: "CONVERSATION");

            migrationBuilder.DropTable(
                name: "PAYMENT");

            migrationBuilder.DropTable(
                name: "POST");

            migrationBuilder.DropTable(
                name: "TOURNAMENT");

            migrationBuilder.DropTable(
                name: "BOOKING");

            migrationBuilder.DropTable(
                name: "SOCIAL_GROUP");

            migrationBuilder.DropTable(
                name: "COURT");

            migrationBuilder.DropTable(
                name: "MATCH");

            migrationBuilder.DropTable(
                name: "VENUE");

            migrationBuilder.DropTable(
                name: "TEAM");

            migrationBuilder.DropTable(
                name: "VENUE_OWNER");

            migrationBuilder.DropTable(
                name: "PLAYER");

            migrationBuilder.DropTable(
                name: "USER");
        }
    }
}
