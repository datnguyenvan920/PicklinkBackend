using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Models;

namespace PicklinkBackend.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Amenity> Amenities { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BookingStatusHistory> BookingStatusHistories { get; set; }

    public virtual DbSet<BookingOperation> BookingOperations { get; set; }

    public virtual DbSet<BookingRule> BookingRules { get; set; }

    public virtual DbSet<Conversation> Conversations { get; set; }

    public virtual DbSet<ConversationParticipant> ConversationParticipants { get; set; }

    public virtual DbSet<Court> Courts { get; set; }

    public virtual DbSet<Friendship> Friendships { get; set; }

    public virtual DbSet<FavoriteVenue> FavoriteVenues { get; set; }

    public virtual DbSet<GroupMember> GroupMembers { get; set; }

    public virtual DbSet<GroupImage> GroupImages { get; set; }

    public virtual DbSet<InventoryItem> InventoryItems { get; set; }

    public virtual DbSet<MarketplaceProvider> MarketplaceProviders { get; set; }

    public virtual DbSet<Match> Matches { get; set; }

    public virtual DbSet<MatchAvailabilitySlot> MatchAvailabilitySlots { get; set; }

    public virtual DbSet<MatchCheckIn> MatchCheckIns { get; set; }

    public virtual DbSet<MatchParticipant> MatchParticipants { get; set; }

    public virtual DbSet<MatchPlayerReview> MatchPlayerReviews { get; set; }

    public virtual DbSet<MatchSlotVote> MatchSlotVotes { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<NotificationLog> NotificationLogs { get; set; }

    public virtual DbSet<OwnerBankAccount> OwnerBankAccounts { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentStatusHistory> PaymentStatusHistories { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<Player> Players { get; set; }

    public virtual DbSet<PlayerTeamRoster> PlayerTeamRosters { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<PostComment> PostComments { get; set; }

    public virtual DbSet<PostLike> PostLikes { get; set; }

    public virtual DbSet<PostMedia> PostMedia { get; set; }

    public virtual DbSet<RatingHistory> RatingHistories { get; set; }

    public virtual DbSet<Scorecard> Scorecards { get; set; }

    public virtual DbSet<SkillMatchup> SkillMatchups { get; set; }

    public virtual DbSet<SocialGroup> SocialGroups { get; set; }

    public virtual DbSet<Staff> Staff { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<Tournament> Tournaments { get; set; }

    public virtual DbSet<TournamentDivision> TournamentDivisions { get; set; }

    public virtual DbSet<TournamentRegistration> TournamentRegistrations { get; set; }

    public virtual DbSet<TournamentPayment> TournamentPayments { get; set; }

    public virtual DbSet<TournamentMatch> TournamentMatches { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Venue> Venues { get; set; }

    public virtual DbSet<VenueImage> VenueImages { get; set; }

    public virtual DbSet<VenueAuditLog> VenueAuditLogs { get; set; }

    public virtual DbSet<VenueOwner> VenueOwners { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Amenity>(entity =>
        {
            entity.ToTable("AMENITY");

            entity.Property(e => e.AmenityId).HasColumnName("amenityId");
            entity.Property(e => e.AmenityName)
                .HasMaxLength(200)
                .HasColumnName("amenityName");
            entity.Property(e => e.IsFree)
                .HasDefaultValue(true)
                .HasColumnName("isFree");
            entity.Property(e => e.VenueId).HasColumnName("venueId");

            entity.HasOne(d => d.Venue).WithMany(p => p.Amenities)
                .HasForeignKey(d => d.VenueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AMENITY_VENUE");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("BOOKING");

            entity.HasIndex(e => e.CourtId, "IX_BOOKING_courtId");

            entity.HasIndex(e => e.MatchId, "IX_BOOKING_matchId");

            entity.HasIndex(e => e.PlayerId, "IX_BOOKING_playerId");

            entity.HasIndex(e => e.StartTime, "IX_BOOKING_startTime");

            entity.Property(e => e.BookingId).HasColumnName("bookingId");
            entity.Property(e => e.CourtId).HasColumnName("courtId");
            entity.Property(e => e.EndTime)
                .HasColumnType("datetime")
                .HasColumnName("endTime");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.StartTime)
                .HasColumnType("datetime")
                .HasColumnName("startTime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending")
                .HasColumnName("status");
            entity.Property(e => e.OwnerEntryType)
                .HasMaxLength(30)
                .HasColumnName("ownerEntryType");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.BookingCode).HasMaxLength(30).HasColumnName("bookingCode");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getutcdate())")
                .ValueGeneratedOnAdd()
                .HasColumnName("createdAt");
            entity.Property(e => e.HoldExpiresAt).HasColumnType("datetime").HasColumnName("holdExpiresAt");
            entity.Property(e => e.HourlyPriceSnapshot).HasColumnName("hourlyPriceSnapshot");
            entity.Property(e => e.CourtAmount).HasColumnName("courtAmount");
            entity.Property(e => e.TotalAmount).HasColumnName("totalAmount");

            entity.HasOne(d => d.Court).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.CourtId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BOOKING_COURT");

            entity.HasOne(d => d.Match).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.MatchId)
                .HasConstraintName("FK_BOOKING_MATCH");

            entity.HasOne(d => d.Player).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.PlayerId)
                .HasConstraintName("FK_BOOKING_PLAYER");
        });

        modelBuilder.Entity<BookingRule>(entity =>
        {
            entity.HasKey(e => e.RuleId);

            entity.ToTable("BOOKING_RULES");

            entity.Property(e => e.RuleId).HasColumnName("ruleId");
            entity.Property(e => e.RuleContent)
                .HasColumnType("text")
                .HasColumnName("ruleContent");
            entity.Property(e => e.RuleType)
                .HasMaxLength(100)
                .HasColumnName("ruleType");
            entity.Property(e => e.VenueId).HasColumnName("venueId");

            entity.HasOne(d => d.Venue).WithMany(p => p.BookingRules)
                .HasForeignKey(d => d.VenueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BOOKING_RULES_VENUE");
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("CONVERSATION");

            entity.HasIndex(e => e.GroupId, "IX_CONVERSATION_groupId").HasFilter("([groupId] IS NOT NULL)");

            entity.HasIndex(e => e.LastMessageAt, "IX_CONVERSATION_lastMessageAt").IsDescending();

            entity.Property(e => e.ConversationId).HasColumnName("conversationId");
            entity.Property(e => e.GroupId).HasColumnName("groupId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.ConversationName)
                .HasMaxLength(200)
                .HasColumnName("conversationName");
            entity.Property(e => e.ConversationType)
                .HasMaxLength(50)
                .HasDefaultValue("Direct")
                .HasColumnName("conversationType");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.LastMessageAt)
                .HasColumnType("datetime")
                .HasColumnName("lastMessageAt");

            entity.HasOne(d => d.Group).WithMany(p => p.Conversations)
                .HasForeignKey(d => d.GroupId)
                .HasConstraintName("FK_CONVERSATION_SOCIAL_GROUP");

            entity.HasOne(d => d.Match).WithMany(p => p.Conversations)
                .HasForeignKey(d => d.MatchId)
                .HasConstraintName("FK_CONVERSATION_MATCH");
        });

        modelBuilder.Entity<ConversationParticipant>(entity =>
        {
            entity.HasKey(e => new { e.ConversationId, e.UserId });

            entity.ToTable("CONVERSATION_PARTICIPANT");

            entity.HasIndex(e => e.UserId, "IX_CONV_PARTICIPANT_userId");

            entity.Property(e => e.ConversationId).HasColumnName("conversationId");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("joinedAt");
            entity.Property(e => e.LastReadAt)
                .HasColumnType("datetime")
                .HasColumnName("lastReadAt");

            entity.HasOne(d => d.Conversation).WithMany(p => p.ConversationParticipants)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONV_PARTICIPANT_CONVERSATION");

            entity.HasOne(d => d.User).WithMany(p => p.ConversationParticipants)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CONV_PARTICIPANT_USER");
        });

        modelBuilder.Entity<Court>(entity =>
        {
            entity.ToTable("COURT");

            entity.HasIndex(e => e.VenueId, "IX_COURT_venueId");

            entity.Property(e => e.CourtId).HasColumnName("courtId");
            entity.Property(e => e.AvailabilityStatus)
                .HasMaxLength(50)
                .HasDefaultValue("Available")
                .HasColumnName("availabilityStatus");
            entity.Property(e => e.CourtNumber).HasColumnName("courtNumber");
            entity.Property(e => e.IsIndoor).HasColumnName("isIndoor");
            entity.Property(e => e.SurfaceType)
                .HasMaxLength(100)
                .HasColumnName("surfaceType");
            entity.Property(e => e.CourtType)
                .HasMaxLength(100)
                .HasDefaultValue("Standard")
                .HasColumnName("courtType");
            entity.Property(e => e.HourlyPrice).HasColumnName("hourlyPrice");
            entity.Property(e => e.VenueId).HasColumnName("venueId");

            entity.HasOne(d => d.Venue).WithMany(p => p.Courts)
                .HasForeignKey(d => d.VenueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_COURT_VENUE");
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.ToTable("FRIENDSHIP");

            entity.HasIndex(e => new { e.ReceiverId, e.Status }, "IX_FRIENDSHIP_receiver");

            entity.HasIndex(e => new { e.RequesterId, e.Status }, "IX_FRIENDSHIP_requester");

            entity.HasIndex(e => new { e.RequesterId, e.ReceiverId }, "UQ_FRIENDSHIP_PAIR").IsUnique();

            entity.Property(e => e.FriendshipId).HasColumnName("friendshipId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.ReceiverId).HasColumnName("receiverId");
            entity.Property(e => e.RequesterId).HasColumnName("requesterId");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updatedAt");

            entity.HasOne(d => d.Receiver).WithMany(p => p.FriendshipReceivers)
                .HasForeignKey(d => d.ReceiverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FRIENDSHIP_RECEIVER");

            entity.HasOne(d => d.Requester).WithMany(p => p.FriendshipRequesters)
                .HasForeignKey(d => d.RequesterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FRIENDSHIP_REQUESTER");
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => new { e.GroupId, e.UserId });

            entity.ToTable("GROUP_MEMBER");

            entity.HasIndex(e => new { e.UserId, e.Status }, "IX_GROUP_MEMBER_userId");

            entity.Property(e => e.GroupId).HasColumnName("groupId");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("joinedAt");
            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .HasDefaultValue("Member")
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Accepted")
                .HasColumnName("status");

            entity.HasOne(d => d.Group).WithMany(p => p.GroupMembers)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GROUP_MEMBER_GROUP");

            entity.HasOne(d => d.User).WithMany(p => p.GroupMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GROUP_MEMBER_USER");
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.ItemId);

            entity.ToTable("INVENTORY_ITEM");

            entity.Property(e => e.ItemId).HasColumnName("itemId");
            entity.Property(e => e.ItemName)
                .HasMaxLength(200)
                .HasColumnName("itemName");
            entity.Property(e => e.PricePerUnit).HasColumnName("pricePerUnit");
            entity.Property(e => e.ProviderId).HasColumnName("providerId");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Available")
                .HasColumnName("status");

            entity.HasOne(d => d.Provider).WithMany(p => p.InventoryItems)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_INVENTORY_ITEM_PROVIDER");
        });

        modelBuilder.Entity<MarketplaceProvider>(entity =>
        {
            entity.HasKey(e => e.ProviderId);

            entity.ToTable("MARKETPLACE_PROVIDER");

            entity.HasIndex(e => e.UserId, "IX_MARKETPLACE_PROVIDER_userId");

            entity.Property(e => e.ProviderId).HasColumnName("providerId");
            entity.Property(e => e.ProviderType)
                .HasMaxLength(100)
                .HasColumnName("providerType");
            entity.Property(e => e.Specialty)
                .HasMaxLength(200)
                .HasColumnName("specialty");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.MarketplaceProviders)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MARKETPLACE_PROVIDER_USER");
        });

        modelBuilder.Entity<Match>(entity =>
        {
            entity.ToTable("MATCH");

            entity.HasIndex(e => e.MatchTime, "IX_MATCH_matchTime");

            entity.HasIndex(e => e.Status, "IX_MATCH_status");

            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.HostPlayerId).HasColumnName("hostPlayerId");
            entity.Property(e => e.MatchSkillLevel).HasColumnName("matchSkillLevel");
            entity.Property(e => e.RequiredPlayerCount).HasDefaultValue(2).HasColumnName("requiredPlayerCount");
            entity.Property(e => e.MatchTime)
                .HasColumnType("datetime")
                .HasColumnName("matchTime");
            entity.Property(e => e.MatchType)
                .HasMaxLength(100)
                .HasColumnName("matchType");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Scheduled")
                .HasColumnName("status");
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.Note).HasMaxLength(1000).HasColumnName("note");
            entity.Property(e => e.Province).HasMaxLength(100).HasColumnName("province");
            entity.Property(e => e.Ward).HasMaxLength(150).HasColumnName("ward");
            entity.Property(e => e.SearchRadiusKm).HasDefaultValue(5d).HasColumnName("searchRadiusKm");
            entity.Property(e => e.SearchLatitude).HasColumnName("searchLatitude");
            entity.Property(e => e.SearchLongitude).HasColumnName("searchLongitude");
            entity.Property(e => e.AvailableDateFrom).HasColumnType("date").HasColumnName("availableDateFrom");
            entity.Property(e => e.AvailableDateTo).HasColumnType("date").HasColumnName("availableDateTo");
            entity.Property(e => e.MinSkillLevel).HasDefaultValue(1).HasColumnName("minSkillLevel");
            entity.Property(e => e.MaxSkillLevel).HasDefaultValue(5).HasColumnName("maxSkillLevel");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("createdAt");
            entity.Property(e => e.CancelledAt).HasColumnType("datetime").HasColumnName("cancelledAt");
            entity.Property(e => e.Team1Id).HasColumnName("team1Id");
            entity.Property(e => e.Team2Id).HasColumnName("team2Id");
            entity.Property(e => e.WinningTeamId).HasColumnName("winningTeamId");

            entity.Property(e => e.PreferredTimeStart).HasColumnName("preferredTimeStart");
            entity.Property(e => e.PreferredTimeEnd).HasColumnName("preferredTimeEnd");
            entity.Property(e => e.SharedVenues).HasMaxLength(500).HasColumnName("sharedVenues");

            entity.HasOne(d => d.Team1).WithMany(p => p.MatchTeam1s)
                .HasForeignKey(d => d.Team1Id)
                .HasConstraintName("FK_MATCH_TEAM1");

            entity.HasOne(d => d.Team2).WithMany(p => p.MatchTeam2s)
                .HasForeignKey(d => d.Team2Id)
                .HasConstraintName("FK_MATCH_TEAM2");

            entity.HasOne(d => d.WinningTeam).WithMany(p => p.MatchWinningTeams)
                .HasForeignKey(d => d.WinningTeamId)
                .HasConstraintName("FK_MATCH_WINNER");

            entity.HasOne(d => d.HostPlayer).WithMany(p => p.HostedMatches)
                .HasForeignKey(d => d.HostPlayerId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_MATCH_HOST_PLAYER");
        });

        modelBuilder.Entity<MatchAvailabilitySlot>(entity =>
        {
            entity.HasKey(e => e.MatchAvailabilitySlotId);

            entity.ToTable("MATCH_AVAILABILITY_SLOT", table =>
                table.HasCheckConstraint("CK_MATCH_AVAILABILITY_SLOT_time", "[timeEnd] > [timeStart]"));

            entity.HasIndex(e => e.MatchId, "IX_MATCH_AVAILABILITY_SLOT_matchId");

            entity.HasIndex(
                    e => new { e.MatchId, e.TimeStart, e.TimeEnd },
                    "UQ_MATCH_AVAILABILITY_SLOT")
                .IsUnique();

            entity.Property(e => e.MatchAvailabilitySlotId).HasColumnName("matchAvailabilitySlotId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.TimeStart).HasColumnType("time").HasColumnName("timeStart");
            entity.Property(e => e.TimeEnd).HasColumnType("time").HasColumnName("timeEnd");

            entity.HasOne(e => e.Match).WithMany(e => e.AvailabilitySlots)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MATCH_AVAILABILITY_SLOT_MATCH");
        });

        modelBuilder.Entity<MatchCheckIn>(entity =>
        {
            entity.HasKey(e => e.CheckInId);

            entity.ToTable("MATCH_CHECKIN");

            entity.HasIndex(e => e.MatchId, "IX_MATCH_CHECKIN_matchId");

            entity.HasIndex(e => e.PlayerId, "IX_MATCH_CHECKIN_playerId");

            entity.HasIndex(e => new { e.MatchId, e.PlayerId }, "UQ_MATCH_CHECKIN_UNIQUE").IsUnique();

            entity.Property(e => e.CheckInId).HasColumnName("checkinId");
            entity.Property(e => e.CheckedInAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("checkedInAt");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.StaffId).HasColumnName("staffId");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Present")
                .HasColumnName("status");

            entity.HasOne(d => d.Match).WithMany(p => p.MatchCheckIns)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MATCH_CHECKIN_MATCH");

            entity.HasOne(d => d.Player).WithMany(p => p.MatchCheckIns)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MATCH_CHECKIN_PLAYER");

            entity.HasOne(d => d.Staff).WithMany(p => p.MatchCheckIns)
                .HasForeignKey(d => d.StaffId)
                .HasConstraintName("FK_MATCH_CHECKIN_STAFF");
        });

        modelBuilder.Entity<MatchParticipant>(entity =>
        {
            entity.HasKey(e => e.ParticipantId);

            entity.ToTable("MATCH_PARTICIPANT");

            entity.HasIndex(e => e.MatchId, "IX_MATCH_PARTICIPANT_match");

            entity.HasIndex(e => e.PlayerId, "IX_MATCH_PARTICIPANT_player");

            entity.HasIndex(e => new { e.MatchId, e.PlayerId }, "UQ_MATCH_PARTICIPANT_match_player").IsUnique();

            entity.Property(e => e.ParticipantId).HasColumnName("participantId");
            entity.Property(e => e.Class)
                .HasMaxLength(100)
                .HasColumnName("class");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.Status).HasMaxLength(30).HasDefaultValue("Accepted").HasColumnName("status");
            entity.Property(e => e.IsHost).HasDefaultValue(false).HasColumnName("isHost");
            entity.Property(e => e.RequestedAt).HasColumnType("datetime").HasColumnName("requestedAt");
            entity.Property(e => e.RespondedAt).HasColumnType("datetime").HasColumnName("respondedAt");

            entity.Property(e => e.VotedVenueId).HasColumnName("votedVenueId");
            entity.Property(e => e.VotedStartTime).HasColumnName("votedStartTime");
            entity.Property(e => e.VotedEndTime).HasColumnName("votedEndTime");

            entity.HasOne(d => d.Match).WithMany(p => p.MatchParticipants)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MATCH_PARTICIPANT_MATCH");

            entity.HasOne(d => d.Player).WithMany(p => p.MatchParticipants)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MATCH_PARTICIPANT_PLAYER");
        });

        modelBuilder.Entity<MatchPlayerReview>(entity =>
        {
            entity.ToTable("MATCH_PLAYER_REVIEW");
            entity.HasKey(e => e.MatchPlayerReviewId);
            entity.HasIndex(e => new { e.MatchId, e.ReviewerPlayerId, e.RevieweePlayerId }, "UQ_MATCH_PLAYER_REVIEW")
                .IsUnique();
            entity.HasIndex(e => e.RevieweePlayerId, "IX_MATCH_PLAYER_REVIEW_revieweePlayerId");
            entity.Property(e => e.MatchPlayerReviewId).HasColumnName("matchPlayerReviewId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.ReviewerPlayerId).HasColumnName("reviewerPlayerId");
            entity.Property(e => e.RevieweePlayerId).HasColumnName("revieweePlayerId");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.Comment).HasMaxLength(1000).HasColumnName("comment");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("createdAt");

            entity.HasOne(e => e.Match).WithMany(e => e.MatchPlayerReviews)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MATCH_PLAYER_REVIEW_MATCH");
            entity.HasOne(e => e.ReviewerPlayer).WithMany(e => e.MatchReviewsWritten)
                .HasForeignKey(e => e.ReviewerPlayerId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_MATCH_PLAYER_REVIEW_REVIEWER");
            entity.HasOne(e => e.RevieweePlayer).WithMany(e => e.MatchReviewsReceived)
                .HasForeignKey(e => e.RevieweePlayerId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_MATCH_PLAYER_REVIEW_REVIEWEE");
        });

        modelBuilder.Entity<MatchSlotVote>(entity =>
        {
            entity.ToTable("MATCH_SLOT_VOTE");
            entity.HasKey(e => e.MatchSlotVoteId);
            entity.HasIndex(e => e.MatchId, "IX_MATCH_SLOT_VOTE_matchId");
            entity.HasIndex(e => new { e.CourtId, e.StartTime, e.EndTime }, "IX_MATCH_SLOT_VOTE_court_time");
            entity.HasIndex(e => new { e.MatchId, e.PlayerId, e.CourtId, e.StartTime, e.EndTime }, "UQ_MATCH_SLOT_VOTE_player_slot")
                .IsUnique();

            entity.Property(e => e.MatchSlotVoteId).HasColumnName("matchSlotVoteId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.CourtId).HasColumnName("courtId");
            entity.Property(e => e.StartTime).HasColumnType("datetime").HasColumnName("startTime");
            entity.Property(e => e.EndTime).HasColumnType("datetime").HasColumnName("endTime");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("createdAt");

            entity.HasOne(e => e.Match).WithMany()
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MATCH_SLOT_VOTE_MATCH");
            entity.HasOne(e => e.Player).WithMany()
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MATCH_SLOT_VOTE_PLAYER");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("MESSAGE");

            entity.HasIndex(e => new { e.ConversationId, e.SentAt }, "IX_MESSAGE_conversationId").IsDescending(false, true);

            entity.HasIndex(e => e.SenderId, "IX_MESSAGE_senderId");

            entity.Property(e => e.MessageId).HasColumnName("messageId");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.ConversationId).HasColumnName("conversationId");
            entity.Property(e => e.IsDeleted).HasColumnName("isDeleted");
            entity.Property(e => e.IsPinned).HasColumnName("isPinned");
            entity.Property(e => e.MediaUrl)
                .HasMaxLength(500)
                .HasColumnName("mediaUrl");
            entity.Property(e => e.MessageType)
                .HasMaxLength(50)
                .HasDefaultValue("Text")
                .HasColumnName("messageType");
            entity.Property(e => e.ReplyToMessageId).HasColumnName("replyToMessageId");
            entity.Property(e => e.SenderId).HasColumnName("senderId");
            entity.Property(e => e.SentAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("sentAt");

            entity.HasOne(d => d.Conversation).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MESSAGE_CONVERSATION");

            entity.HasOne(d => d.ReplyToMessage).WithMany(p => p.InverseReplyToMessage)
                .HasForeignKey(d => d.ReplyToMessageId)
                .HasConstraintName("FK_MESSAGE_REPLY");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MESSAGE_SENDER");
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(e => e.NotifId);

            entity.ToTable("NOTIFICATION_LOG");

            entity.HasIndex(e => e.UserId, "IX_NOTIF_userId");

            entity.Property(e => e.NotifId).HasColumnName("notifId");
            entity.Property(e => e.IsRead).HasColumnName("isRead");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(30)
                .HasDefaultValue("system")
                .HasColumnName("notificationType");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasDefaultValue("Thông báo")
                .HasColumnName("title");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Tone)
                .HasMaxLength(20)
                .HasDefaultValue("default")
                .HasColumnName("tone");
            entity.Property(e => e.LinkTo)
                .HasMaxLength(500)
                .HasColumnName("linkTo");
            entity.Property(e => e.LinkLabel)
                .HasMaxLength(100)
                .HasColumnName("linkLabel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("createdAt");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NOTIFICATION_LOG_USER");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("PAYMENT");

            entity.HasIndex(e => e.BookingId, "IX_PAYMENT_bookingId");

            entity.HasIndex(e => e.PayerId, "IX_PAYMENT_payerId");

            entity.HasIndex(e => e.PaymentGroupId, "IX_PAYMENT_paymentGroupId");

            entity.HasIndex(e => e.TransferCode, "UQ_PAYMENT_transferCode").IsUnique().HasFilter("[transferCode] IS NOT NULL");

            entity.Property(e => e.PaymentId).HasColumnName("paymentId");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.BookingId).HasColumnName("bookingId");
            entity.Property(e => e.PaidAt)
                .HasColumnType("datetime")
                .HasColumnName("paidAt");
            entity.Property(e => e.PayerId).HasColumnName("payerId");
            entity.Property(e => e.PaymentGroupId).HasColumnName("paymentGroupId");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(100)
                .HasColumnName("paymentMethod");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending")
                .HasColumnName("status");
            entity.Property(e => e.TransferCode).HasMaxLength(40).HasColumnName("transferCode");
            entity.Property(e => e.TransferContent).HasMaxLength(140).HasColumnName("transferContent");
            entity.Property(e => e.BankCode).HasMaxLength(30).HasColumnName("bankCode");
            entity.Property(e => e.BankName).HasMaxLength(150).HasColumnName("bankName");
            entity.Property(e => e.BankAccountNumber).HasMaxLength(50).HasColumnName("bankAccountNumber");
            entity.Property(e => e.BankAccountName).HasMaxLength(200).HasColumnName("bankAccountName");
            entity.Property(e => e.QrImageUrl).HasMaxLength(2000).HasColumnName("qrImageUrl");
            entity.Property(e => e.ReceiptImageUrl).HasMaxLength(1000).HasColumnName("receiptImageUrl");
            entity.Property(e => e.SubmittedAt).HasColumnType("datetime").HasColumnName("submittedAt");
            entity.Property(e => e.VerifiedAt).HasColumnType("datetime").HasColumnName("verifiedAt");
            entity.Property(e => e.VerifiedByUserId).HasColumnName("verifiedByUserId");
            entity.Property(e => e.RejectionReason).HasMaxLength(500).HasColumnName("rejectionReason");

            entity.HasOne(d => d.Booking).WithMany(p => p.Payments)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PAYMENT_BOOKING");

            entity.HasOne(d => d.Payer).WithMany(p => p.Payments)
                .HasForeignKey(d => d.PayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PAYMENT_PAYER");
        });

        modelBuilder.Entity<FavoriteVenue>(entity =>
        {
            entity.HasKey(e => new { e.PlayerId, e.VenueId });
            entity.ToTable("FAVORITE_VENUE");
            entity.HasIndex(e => e.VenueId, "IX_FAVORITE_VENUE_venueId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.VenueId).HasColumnName("venueId");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("createdAt");
            entity.HasOne(e => e.Player).WithMany(e => e.FavoriteVenues)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FAVORITE_VENUE_PLAYER");
            entity.HasOne(e => e.Venue).WithMany(e => e.FavoritePlayers)
                .HasForeignKey(e => e.VenueId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FAVORITE_VENUE_VENUE");
        });

        modelBuilder.Entity<BookingOperation>(entity =>
        {
            entity.ToTable("BOOKING_OPERATION");
            entity.HasKey(e => e.BookingOperationId);
            entity.HasIndex(e => e.BookingId, "UQ_BOOKING_OPERATION_bookingId").IsUnique();
            entity.Property(e => e.BookingOperationId).HasColumnName("bookingOperationId");
            entity.Property(e => e.BookingId).HasColumnName("bookingId");
            entity.Property(e => e.CheckInStatus).HasMaxLength(30).HasDefaultValue("Ready").HasColumnName("checkInStatus");
            entity.Property(e => e.CodeVerifiedAt).HasColumnType("datetime").HasColumnName("codeVerifiedAt");
            entity.Property(e => e.CodeVerifiedByUserId).HasColumnName("codeVerifiedByUserId");
            entity.Property(e => e.PaymentConfirmedAt).HasColumnType("datetime").HasColumnName("paymentConfirmedAt");
            entity.Property(e => e.PaymentConfirmedByUserId).HasColumnName("paymentConfirmedByUserId");
            entity.Property(e => e.CheckedInAt).HasColumnType("datetime").HasColumnName("checkedInAt");
            entity.Property(e => e.CheckedInByUserId).HasColumnName("checkedInByUserId");
            entity.Property(e => e.NoShowAt).HasColumnType("datetime").HasColumnName("noShowAt");
            entity.Property(e => e.NoShowByUserId).HasColumnName("noShowByUserId");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasColumnName("updatedAt");
            entity.HasOne(e => e.Booking).WithOne(e => e.Operation)
                .HasForeignKey<BookingOperation>(e => e.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BOOKING_OPERATION_BOOKING");
        });

        modelBuilder.Entity<PaymentStatusHistory>(entity =>
        {
            entity.ToTable("PAYMENT_STATUS_HISTORY");
            entity.HasKey(e => e.PaymentStatusHistoryId);
            entity.HasIndex(e => e.PaymentId, "IX_PAYMENT_STATUS_HISTORY_paymentId");
            entity.Property(e => e.PaymentStatusHistoryId).HasColumnName("paymentStatusHistoryId");
            entity.Property(e => e.PaymentId).HasColumnName("paymentId");
            entity.Property(e => e.FromStatus).HasMaxLength(50).HasColumnName("fromStatus");
            entity.Property(e => e.ToStatus).HasMaxLength(50).HasColumnName("toStatus");
            entity.Property(e => e.Action).HasMaxLength(100).HasColumnName("action");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("reason");
            entity.Property(e => e.ActorUserId).HasColumnName("actorUserId");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("createdAt");
            entity.HasOne(e => e.Payment).WithMany(e => e.StatusHistories)
                .HasForeignKey(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PAYMENT_STATUS_HISTORY_PAYMENT");
        });

        modelBuilder.Entity<OwnerBankAccount>(entity =>
        {
            entity.ToTable("OWNER_BANK_ACCOUNT");
            entity.HasKey(e => e.OwnerBankAccountId);
            entity.HasIndex(e => e.OwnerId, "UQ_OWNER_BANK_ACCOUNT_ownerId").IsUnique();
            entity.Property(e => e.OwnerBankAccountId).HasColumnName("ownerBankAccountId");
            entity.Property(e => e.OwnerId).HasColumnName("ownerId");
            entity.Property(e => e.BankCode).HasMaxLength(30).HasColumnName("bankCode");
            entity.Property(e => e.BankName).HasMaxLength(150).HasColumnName("bankName");
            entity.Property(e => e.AccountNumber).HasMaxLength(50).HasColumnName("accountNumber");
            entity.Property(e => e.AccountHolderName).HasMaxLength(200).HasColumnName("accountHolderName");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("isActive");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("createdAt");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasColumnName("updatedAt");
            entity.HasOne(e => e.Owner).WithMany(e => e.BankAccounts)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_OWNER_BANK_ACCOUNT_OWNER");
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.ResetTokenId);

            entity.ToTable("PASSWORD_RESET_TOKEN");

            entity.HasIndex(e => e.TokenHash, "IX_PASSWORD_RESET_TOKEN_tokenHash");

            entity.HasIndex(e => e.UserId, "IX_PASSWORD_RESET_TOKEN_userId");

            entity.Property(e => e.ResetTokenId).HasColumnName("resetTokenId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime")
                .HasColumnName("expiresAt");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(128)
                .HasColumnName("tokenHash");
            entity.Property(e => e.UsedAt)
                .HasColumnType("datetime")
                .HasColumnName("usedAt");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PASSWORD_RESET_TOKEN_USER");
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("PLAYER");

            entity.HasIndex(e => e.UserId, "IX_PLAYER_userId");

            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.PlayerSubType)
                .HasMaxLength(50)
                .HasColumnName("playerSubType");
            entity.Property(e => e.PlayFrequency)
                .HasMaxLength(50)
                .HasColumnName("playFrequency");
            entity.Property(e => e.PreferredTimeSlot)
                .HasMaxLength(50)
                .HasColumnName("preferredTimeSlot");
            entity.Property(e => e.Bio)
                .HasMaxLength(500)
                .HasColumnName("bio");
            entity.Property(e => e.BirthDate)
                .HasColumnName("birthDate");
            entity.Property(e => e.Gender)
                .HasMaxLength(30)
                .HasColumnName("gender");
            entity.Property(e => e.HeightCm)
                .HasColumnName("heightCm");
            entity.Property(e => e.WeightKg)
                .HasColumnName("weightKg");
            entity.Property(e => e.Prestige).HasColumnName("prestige");
            entity.Property(e => e.SkillLevel).HasColumnName("skillLevel");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.Players)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PLAYER_USER");
        });

        modelBuilder.Entity<PlayerTeamRoster>(entity =>
        {
            entity.HasKey(e => new { e.PlayerId, e.TeamId });

            entity.ToTable("PLAYER_TEAM_ROSTER");

            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.TeamId).HasColumnName("teamId");
            entity.Property(e => e.JoinedDate)
                .HasDefaultValueSql("(CONVERT([date],getdate()))")
                .HasColumnName("joinedDate");

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerTeamRosters)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PTR_PLAYER");

            entity.HasOne(d => d.Team).WithMany(p => p.PlayerTeamRosters)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PTR_TEAM");
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("POST");

            entity.HasIndex(e => e.AuthorId, "IX_POST_authorId");

            entity.HasIndex(e => e.CreatedAt, "IX_POST_createdAt").IsDescending();

            entity.HasIndex(e => e.ExpiresAt, "IX_POST_expiresAt").HasFilter("([expiresAt] IS NOT NULL)");

            entity.HasIndex(e => e.GroupId, "IX_POST_groupId").HasFilter("([groupId] IS NOT NULL)");

            entity.Property(e => e.PostId).HasColumnName("postId");
            entity.Property(e => e.AuthorId).HasColumnName("authorId");
            entity.Property(e => e.GroupId).HasColumnName("groupId");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime")
                .HasColumnName("expiresAt");
            entity.Property(e => e.PostType)
                .HasMaxLength(50)
                .HasDefaultValue("Post")
                .HasColumnName("postType");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updatedAt");
            entity.Property(e => e.Visibility)
                .HasMaxLength(50)
                .HasDefaultValue("Public")
                .HasColumnName("visibility");

            entity.HasOne(d => d.Author).WithMany(p => p.Posts)
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_AUTHOR");

            entity.HasOne(d => d.Group).WithMany(p => p.Posts)
                .HasForeignKey(d => d.GroupId)
                .HasConstraintName("FK_POST_SOCIAL_GROUP");
        });

        modelBuilder.Entity<PostComment>(entity =>
        {
            entity.HasKey(e => e.CommentId);

            entity.ToTable("POST_COMMENT");

            entity.HasIndex(e => e.ParentCommentId, "IX_POST_COMMENT_parent").HasFilter("([parentCommentId] IS NOT NULL)");

            entity.HasIndex(e => new { e.PostId, e.CreatedAt }, "IX_POST_COMMENT_postId");

            entity.Property(e => e.CommentId).HasColumnName("commentId");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.ParentCommentId).HasColumnName("parentCommentId");
            entity.Property(e => e.PostId).HasColumnName("postId");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updatedAt");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.ParentComment).WithMany(p => p.InverseParentComment)
                .HasForeignKey(d => d.ParentCommentId)
                .HasConstraintName("FK_POST_COMMENT_PARENT");

            entity.HasOne(d => d.Post).WithMany(p => p.PostComments)
                .HasForeignKey(d => d.PostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_COMMENT_POST");

            entity.HasOne(d => d.User).WithMany(p => p.PostComments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_COMMENT_USER");
        });

        modelBuilder.Entity<PostLike>(entity =>
        {
            entity.HasKey(e => e.LikeId);

            entity.ToTable("POST_LIKE");

            entity.HasIndex(e => e.PostId, "IX_POST_LIKE_postId");

            entity.HasIndex(e => e.UserId, "IX_POST_LIKE_userId");

            entity.HasIndex(e => new { e.PostId, e.UserId }, "UQ_POST_LIKE_USER_POST").IsUnique();

            entity.Property(e => e.LikeId).HasColumnName("likeId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.PostId).HasColumnName("postId");
            entity.Property(e => e.ReactionType)
                .HasMaxLength(50)
                .HasDefaultValue("Like")
                .HasColumnName("reactionType");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.Post).WithMany(p => p.PostLikes)
                .HasForeignKey(d => d.PostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_LIKE_POST");

            entity.HasOne(d => d.User).WithMany(p => p.PostLikes)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_LIKE_USER");
        });

        modelBuilder.Entity<PostMedia>(entity =>
        {
            entity.HasKey(e => e.MediaId);

            entity.ToTable("POST_MEDIA");

            entity.HasIndex(e => new { e.PostId, e.DisplayOrder }, "IX_POST_MEDIA_postId");

            entity.Property(e => e.MediaId).HasColumnName("mediaId");
            entity.Property(e => e.DisplayOrder).HasColumnName("displayOrder");
            entity.Property(e => e.MediaType)
                .HasMaxLength(50)
                .HasDefaultValue("Image")
                .HasColumnName("mediaType");
            entity.Property(e => e.MediaUrl)
                .HasMaxLength(500)
                .HasColumnName("mediaUrl");
            entity.Property(e => e.PostId).HasColumnName("postId");

            entity.HasOne(d => d.Post).WithMany(p => p.PostMedia)
                .HasForeignKey(d => d.PostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POST_MEDIA_POST");
        });

        modelBuilder.Entity<RatingHistory>(entity =>
        {
            entity.HasKey(e => e.RatingId);

            entity.ToTable("RATING_HISTORY");

            entity.HasIndex(e => new { e.TargetId, e.TargetType }, "IX_RATING_HISTORY_target");

            entity.HasIndex(e => e.UserId, "IX_RATING_HISTORY_userId");

            entity.HasIndex(e => new { e.BookingId, e.UserId }, "UQ_RATING_HISTORY_booking_user")
                .IsUnique()
                .HasFilter("([bookingId] IS NOT NULL)");

            entity.Property(e => e.RatingId).HasColumnName("ratingId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.BookingId).HasColumnName("bookingId");
            entity.Property(e => e.Comment).HasMaxLength(1000).HasColumnName("comment");
            entity.Property(e => e.Tags).HasMaxLength(500).HasColumnName("tags");
            entity.Property(e => e.IsAnonymous).HasDefaultValue(false).HasColumnName("isAnonymous");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.TargetId).HasColumnName("targetId");
            entity.Property(e => e.TargetType)
                .HasMaxLength(50)
                .HasColumnName("targetType");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.RatingHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RATING_HISTORY_USER");

            entity.HasOne(d => d.Booking).WithMany(p => p.Ratings)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RATING_HISTORY_BOOKING");
        });

        modelBuilder.Entity<Scorecard>(entity =>
        {
            entity.HasKey(e => e.GameId);

            entity.ToTable("SCORECARD");

            entity.Property(e => e.GameId).HasColumnName("gameId");
            entity.Property(e => e.CourtId).HasColumnName("courtId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.ScoreInfo).HasColumnName("scoreInfo");

            entity.HasOne(d => d.Court).WithMany(p => p.Scorecards)
                .HasForeignKey(d => d.CourtId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SCORECARD_COURT");

            entity.HasOne(d => d.Match).WithMany(p => p.Scorecards)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SCORECARD_MATCH");
        });

        modelBuilder.Entity<SkillMatchup>(entity =>
        {
            entity.HasKey(e => e.MatchupId);

            entity.ToTable("SKILL_MATCHUP");

            entity.Property(e => e.MatchupId).HasColumnName("matchupId");
            entity.Property(e => e.MatchId).HasColumnName("matchId");
            entity.Property(e => e.PlayerId).HasColumnName("playerId");
            entity.Property(e => e.SkillDelta).HasColumnName("skillDelta");

            entity.HasOne(d => d.Match).WithMany(p => p.SkillMatchups)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SKILL_MATCHUP_MATCH");

            entity.HasOne(d => d.Player).WithMany(p => p.SkillMatchups)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SKILL_MATCHUP_PLAYER");
        });

        modelBuilder.Entity<SocialGroup>(entity =>
        {
            entity.HasKey(e => e.GroupId);

            entity.ToTable("SOCIAL_GROUP");

            entity.HasIndex(e => e.OwnerId, "IX_SOCIAL_GROUP_ownerId");

            entity.Property(e => e.GroupId).HasColumnName("groupId");
            entity.Property(e => e.CoverImageUrl)
                .HasMaxLength(500)
                .HasColumnName("coverImageUrl");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.GroupName)
                .HasMaxLength(200)
                .HasColumnName("groupName");
            entity.Property(e => e.GroupType)
                .HasMaxLength(50)
                .HasDefaultValue("Public")
                .HasColumnName("groupType");
            entity.Property(e => e.OwnerId).HasColumnName("ownerId");
            entity.Property(e => e.Rules).HasColumnName("rules");
            entity.Property(e => e.ActiveLocation)
                .HasMaxLength(255)
                .HasColumnName("activeLocation");
            entity.Property(e => e.OverallRating)
                .HasDefaultValue(0.0)
                .HasColumnName("overallRating");
            entity.Property(e => e.RatingCount)
                .HasDefaultValue(0)
                .HasColumnName("ratingCount");

            entity.HasOne(d => d.Owner).WithMany(p => p.SocialGroups)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SOCIAL_GROUP_OWNER");
        });

        modelBuilder.Entity<GroupImage>(entity =>
        {
            entity.HasKey(e => e.GroupImageId);

            entity.ToTable("GROUP_IMAGE");

            entity.HasIndex(e => new { e.GroupId, e.SortOrder }, "IX_GROUP_IMAGE_groupId");

            entity.Property(e => e.GroupImageId).HasColumnName("groupImageId");
            entity.Property(e => e.GroupId).HasColumnName("groupId");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(1000)
                .HasColumnName("imageUrl");
            entity.Property(e => e.Caption)
                .HasMaxLength(200)
                .HasColumnName("caption");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sortOrder");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");

            entity.HasOne(d => d.Group).WithMany(p => p.GroupImages)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_GROUP_IMAGE_GROUP");
        });

        modelBuilder.Entity<Staff>(entity =>
        {
            entity.ToTable("STAFF");

            entity.HasIndex(e => e.UserId, "IX_STAFF_userId");

            entity.HasIndex(e => e.VenueId, "IX_STAFF_venueId");

            entity.HasIndex(e => new { e.UserId, e.VenueId }, "UQ_STAFF_userId_venueId").IsUnique();

            entity.Property(e => e.StaffId).HasColumnName("staffId");
            entity.Property(e => e.Role)
                .HasMaxLength(100)
                .HasColumnName("role");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.VenueId).HasColumnName("venueId");
            entity.Property(e => e.Permissions).HasMaxLength(500).HasColumnName("permissions");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("isActive");
            entity.Property(e => e.AssignedAt).HasColumnType("datetime").HasColumnName("assignedAt");
            entity.Property(e => e.AssignedByUserId).HasColumnName("assignedByUserId");
            entity.Property(e => e.RevokedAt).HasColumnType("datetime").HasColumnName("revokedAt");

            entity.HasOne(d => d.User).WithMany(p => p.Staff)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_STAFF_USER");

            entity.HasOne(d => d.Venue).WithMany(p => p.Staff)
                .HasForeignKey(d => d.VenueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_STAFF_VENUE");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("TEAM");

            entity.Property(e => e.TeamId).HasColumnName("teamId");
            entity.Property(e => e.CaptainId).HasColumnName("captainId");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.TeamName)
                .HasMaxLength(200)
                .HasColumnName("teamName");

            entity.HasOne(d => d.Captain).WithMany(p => p.Teams)
                .HasForeignKey(d => d.CaptainId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TEAM_CAPTAIN");
        });

        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.ToTable("TOURNAMENT");

            entity.HasIndex(e => e.Slug, "UQ_TOURNAMENT_slug").IsUnique();

            entity.Property(e => e.TournamentId).HasColumnName("tournamentId");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.ApprovedAt).HasColumnName("approvedAt");
            entity.Property(e => e.ApprovedByUserId).HasColumnName("approvedByUserId");
            entity.Property(e => e.BracketType)
                .HasMaxLength(100)
                .HasColumnName("bracketType");
            entity.Property(e => e.Capacity).HasColumnName("capacity");
            entity.Property(e => e.City)
                .HasMaxLength(100)
                .HasColumnName("city");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("createdAt");
            entity.Property(e => e.CreatedByUserId).HasColumnName("createdByUserId");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EndDate).HasColumnName("endDate");
            entity.Property(e => e.EntryFee)
                .HasColumnType("decimal(18,2)")
                .HasColumnName("entryFee");
            entity.Property(e => e.Format)
                .HasMaxLength(100)
                .HasColumnName("format");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(1000)
                .HasColumnName("imageUrl");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.OrganizerName)
                .HasMaxLength(200)
                .HasColumnName("organizerName");
            entity.Property(e => e.OrganizerPhone)
                .HasMaxLength(30)
                .HasColumnName("organizerPhone");
            entity.Property(e => e.PrizePool)
                .HasColumnType("decimal(18,2)")
                .HasColumnName("prizePool");
            entity.Property(e => e.RegistrationDeadline).HasColumnName("registrationDeadline");
            entity.Property(e => e.ResultsPublishedAt).HasColumnName("resultsPublishedAt");
            entity.Property(e => e.Rules).HasColumnName("rules");
            entity.Property(e => e.SkillLevel)
                .HasMaxLength(100)
                .HasColumnName("skillLevel");
            entity.Property(e => e.Slug)
                .HasMaxLength(220)
                .HasColumnName("slug");
            entity.Property(e => e.StartDate).HasColumnName("startDate");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Draft")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("updatedAt");
            entity.Property(e => e.VenueName)
                .HasMaxLength(200)
                .HasColumnName("venueName");

            entity.HasMany(d => d.Teams).WithMany(p => p.Tournaments)
                .UsingEntity<Dictionary<string, object>>(
                    "TournamentTeam",
                    r => r.HasOne<Team>().WithMany()
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_TOURNAMENT_TEAM_TEAM"),
                    l => l.HasOne<Tournament>().WithMany()
                        .HasForeignKey("TournamentId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_TOURNAMENT_TEAM_TOURN"),
                    j =>
                    {
                        j.HasKey("TournamentId", "TeamId");
                        j.ToTable("TOURNAMENT_TEAM");
                        j.IndexerProperty<int>("TournamentId").HasColumnName("tournamentId");
                        j.IndexerProperty<int>("TeamId").HasColumnName("teamId");
                    });
        });

        modelBuilder.Entity<TournamentDivision>(entity =>
        {
            entity.ToTable("TOURNAMENT_DIVISION");

            entity.HasIndex(e => new { e.TournamentId, e.Name }, "UQ_TOURNAMENT_DIVISION_name").IsUnique();

            entity.Property(e => e.TournamentDivisionId).HasColumnName("tournamentDivisionId");
            entity.Property(e => e.Capacity).HasColumnName("capacity");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.DisplayOrder).HasColumnName("displayOrder");
            entity.Property(e => e.EntryFee)
                .HasColumnType("decimal(18,2)")
                .HasColumnName("entryFee");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.SkillLevel)
                .HasMaxLength(100)
                .HasColumnName("skillLevel");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Open")
                .HasColumnName("status");
            entity.Property(e => e.TournamentId).HasColumnName("tournamentId");

            entity.HasOne(e => e.Tournament)
                .WithMany(e => e.Divisions)
                .HasForeignKey(e => e.TournamentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TOURNAMENT_DIVISION_TOURNAMENT");
        });

        modelBuilder.Entity<TournamentRegistration>(entity =>
        {
            entity.ToTable("TOURNAMENT_REGISTRATION");

            entity.HasIndex(
                e => new { e.TournamentId, e.CaptainPlayerId },
                "UQ_TOURNAMENT_REGISTRATION_captain").IsUnique();
            entity.HasIndex(e => e.CheckInCode, "UQ_TOURNAMENT_REGISTRATION_checkInCode")
                .IsUnique()
                .HasFilter("[checkInCode] IS NOT NULL");

            entity.Property(e => e.TournamentRegistrationId).HasColumnName("tournamentRegistrationId");
            entity.Property(e => e.AmountDue)
                .HasColumnType("decimal(18,2)")
                .HasColumnName("amountDue");
            entity.Property(e => e.CaptainPlayerId).HasColumnName("captainPlayerId");
            entity.Property(e => e.CheckedInAt).HasColumnName("checkedInAt");
            entity.Property(e => e.CheckedInByUserId).HasColumnName("checkedInByUserId");
            entity.Property(e => e.CheckInCode)
                .HasMaxLength(40)
                .HasColumnName("checkInCode");
            entity.Property(e => e.PartnerName)
                .HasMaxLength(200)
                .HasColumnName("partnerName");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Unpaid")
                .HasColumnName("paymentStatus");
            entity.Property(e => e.RegisteredAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("registeredAt");
            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500)
                .HasColumnName("rejectionReason");
            entity.Property(e => e.RepresentativePhone)
                .HasMaxLength(30)
                .HasColumnName("representativePhone");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewedAt");
            entity.Property(e => e.ReviewedByUserId).HasColumnName("reviewedByUserId");
            entity.Property(e => e.Seed).HasColumnName("seed");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Pending")
                .HasColumnName("status");
            entity.Property(e => e.TeamName)
                .HasMaxLength(200)
                .HasColumnName("teamName");
            entity.Property(e => e.TournamentDivisionId).HasColumnName("tournamentDivisionId");
            entity.Property(e => e.TournamentId).HasColumnName("tournamentId");

            entity.HasOne(e => e.Tournament)
                .WithMany(e => e.Registrations)
                .HasForeignKey(e => e.TournamentId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_REGISTRATION_TOURNAMENT");
            entity.HasOne(e => e.Division)
                .WithMany(e => e.Registrations)
                .HasForeignKey(e => e.TournamentDivisionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_REGISTRATION_DIVISION");
            entity.HasOne(e => e.CaptainPlayer)
                .WithMany(e => e.TournamentRegistrations)
                .HasForeignKey(e => e.CaptainPlayerId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_REGISTRATION_PLAYER");
        });

        modelBuilder.Entity<TournamentPayment>(entity =>
        {
            entity.ToTable("TOURNAMENT_PAYMENT");

            entity.HasIndex(e => e.TournamentRegistrationId, "UQ_TOURNAMENT_PAYMENT_registration").IsUnique();

            entity.Property(e => e.TournamentPaymentId).HasColumnName("tournamentPaymentId");
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18,2)")
                .HasColumnName("amount");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasColumnName("paymentMethod");
            entity.Property(e => e.ReceiptImageUrl)
                .HasMaxLength(1000)
                .HasColumnName("receiptImageUrl");
            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500)
                .HasColumnName("rejectionReason");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Pending")
                .HasColumnName("status");
            entity.Property(e => e.SubmittedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("submittedAt");
            entity.Property(e => e.TournamentRegistrationId).HasColumnName("tournamentRegistrationId");
            entity.Property(e => e.TransferContent)
                .HasMaxLength(250)
                .HasColumnName("transferContent");
            entity.Property(e => e.VerifiedAt).HasColumnName("verifiedAt");
            entity.Property(e => e.VerifiedByUserId).HasColumnName("verifiedByUserId");

            entity.HasOne(e => e.Registration)
                .WithOne(e => e.Payment)
                .HasForeignKey<TournamentPayment>(e => e.TournamentRegistrationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TOURNAMENT_PAYMENT_REGISTRATION");
        });

        modelBuilder.Entity<TournamentMatch>(entity =>
        {
            entity.ToTable("TOURNAMENT_MATCH");

            entity.HasIndex(
                e => new { e.TournamentDivisionId, e.RoundName, e.MatchNumber },
                "UQ_TOURNAMENT_MATCH_round").IsUnique();

            entity.Property(e => e.TournamentMatchId).HasColumnName("tournamentMatchId");
            entity.Property(e => e.CourtName)
                .HasMaxLength(100)
                .HasColumnName("courtName");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("createdAt");
            entity.Property(e => e.MatchNumber).HasColumnName("matchNumber");
            entity.Property(e => e.Notes)
                .HasMaxLength(1000)
                .HasColumnName("notes");
            entity.Property(e => e.RoundName)
                .HasMaxLength(100)
                .HasColumnName("roundName");
            entity.Property(e => e.ScheduledAt).HasColumnName("scheduledAt");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Scheduled")
                .HasColumnName("status");
            entity.Property(e => e.Team1RegistrationId).HasColumnName("team1RegistrationId");
            entity.Property(e => e.Team1Score).HasColumnName("team1Score");
            entity.Property(e => e.Team2RegistrationId).HasColumnName("team2RegistrationId");
            entity.Property(e => e.Team2Score).HasColumnName("team2Score");
            entity.Property(e => e.TournamentDivisionId).HasColumnName("tournamentDivisionId");
            entity.Property(e => e.TournamentId).HasColumnName("tournamentId");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnName("updatedAt");
            entity.Property(e => e.WinnerRegistrationId).HasColumnName("winnerRegistrationId");

            entity.HasOne(e => e.Tournament)
                .WithMany(e => e.Matches)
                .HasForeignKey(e => e.TournamentId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_MATCH_TOURNAMENT");
            entity.HasOne(e => e.Division)
                .WithMany(e => e.Matches)
                .HasForeignKey(e => e.TournamentDivisionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_MATCH_DIVISION");
            entity.HasOne(e => e.Team1Registration)
                .WithMany(e => e.Team1Matches)
                .HasForeignKey(e => e.Team1RegistrationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_MATCH_TEAM1");
            entity.HasOne(e => e.Team2Registration)
                .WithMany(e => e.Team2Matches)
                .HasForeignKey(e => e.Team2RegistrationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_MATCH_TEAM2");
            entity.HasOne(e => e.WinnerRegistration)
                .WithMany(e => e.WonMatches)
                .HasForeignKey(e => e.WinnerRegistrationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TOURNAMENT_MATCH_WINNER");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("USER");

            entity.HasIndex(e => e.Email, "UQ_USER_email").IsUnique();

            entity.HasIndex(e => e.Username, "UQ_USER_username").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.City)
                .HasMaxLength(100)
                .HasColumnName("city");
            entity.Property(e => e.Commune)
                .HasMaxLength(150)
                .HasColumnName("commune");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(512)
                .HasColumnName("passwordHash");
            entity.Property(e => e.ProfileImageUrl)
                .HasMaxLength(500)
                .HasColumnName("profileImageUrl");
            entity.Property(e => e.UserType)
                .HasMaxLength(50)
                .HasColumnName("userType");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username");
        });

        modelBuilder.Entity<Venue>(entity =>
        {
            entity.ToTable("VENUE");

            entity.HasIndex(e => e.OwnerId, "IX_VENUE_ownerId");

            entity.Property(e => e.VenueId).HasColumnName("venueId");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.CloseTime).HasColumnName("closeTime");
            entity.Property(e => e.OpenTime).HasColumnName("openTime");
            entity.Property(e => e.OverallRating).HasColumnName("overallRating");
            entity.Property(e => e.OwnerId).HasColumnName("ownerId");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phoneNumber");
            entity.Property(e => e.VenueName)
                .HasMaxLength(200)
                .HasColumnName("venueName");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.IsOpen)
                .HasDefaultValue(true)
                .HasColumnName("isOpen");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Draft")
                .HasColumnName("approvalStatus");
            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500)
                .HasColumnName("rejectionReason");

            entity.HasOne(d => d.Owner).WithMany(p => p.Venues)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VENUE_OWNER");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("BOOKING_STATUS_HISTORY");
            entity.HasKey(e => e.BookingStatusHistoryId);
            entity.HasIndex(e => e.BookingId, "IX_BOOKING_STATUS_HISTORY_bookingId");
            entity.Property(e => e.BookingStatusHistoryId).HasColumnName("bookingStatusHistoryId");
            entity.Property(e => e.BookingId).HasColumnName("bookingId");
            entity.Property(e => e.FromStatus).HasMaxLength(50).HasColumnName("fromStatus");
            entity.Property(e => e.ToStatus).HasMaxLength(50).HasColumnName("toStatus");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("reason");
            entity.Property(e => e.ActorUserId).HasColumnName("actorUserId");
            entity.Property(e => e.ChangedAt).HasColumnType("datetime").HasColumnName("changedAt");
            entity.HasOne(e => e.Booking).WithMany(e => e.StatusHistories)
                .HasForeignKey(e => e.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BOOKING_STATUS_HISTORY_BOOKING");
        });

        modelBuilder.Entity<VenueImage>(entity =>
        {
            entity.ToTable("VENUE_IMAGE");
            entity.HasKey(e => e.VenueImageId);
            entity.HasIndex(e => e.VenueId, "IX_VENUE_IMAGE_venueId");
            entity.Property(e => e.VenueImageId).HasColumnName("venueImageId");
            entity.Property(e => e.VenueId).HasColumnName("venueId");
            entity.Property(e => e.ImageUrl).HasMaxLength(1000).HasColumnName("imageUrl");
            entity.Property(e => e.Caption).HasMaxLength(200).HasColumnName("caption");
            entity.Property(e => e.IsPrimary).HasColumnName("isPrimary");
            entity.Property(e => e.SortOrder).HasColumnName("sortOrder");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("createdAt");

            entity.HasOne(e => e.Venue).WithMany(e => e.VenueImages)
                .HasForeignKey(e => e.VenueId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_VENUE_IMAGE_VENUE");
        });

        modelBuilder.Entity<VenueAuditLog>(entity =>
        {
            entity.HasKey(e => e.LogId);

            entity.ToTable("VENUE_AUDIT_LOG");

            entity.HasIndex(e => e.VenueId, "IX_VENUE_AUDIT_venueId");

            entity.Property(e => e.LogId).HasColumnName("logId");
            entity.Property(e => e.Action)
                .HasMaxLength(500)
                .HasColumnName("action");
            entity.Property(e => e.ActorId).HasColumnName("actorId");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("timestamp");
            entity.Property(e => e.VenueId).HasColumnName("venueId");

            entity.HasOne(d => d.Actor).WithMany(p => p.VenueAuditLogs)
                .HasForeignKey(d => d.ActorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VENUE_AUDIT_LOG_ACTOR");

            entity.HasOne(d => d.Venue).WithMany(p => p.VenueAuditLogs)
                .HasForeignKey(d => d.VenueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VENUE_AUDIT_LOG_VENUE");
        });

        modelBuilder.Entity<VenueOwner>(entity =>
        {
            entity.HasKey(e => e.OwnerId);

            entity.ToTable("VENUE_OWNER");

            entity.HasIndex(e => e.UserId, "IX_VENUE_OWNER_userId");

            entity.Property(e => e.OwnerId).HasColumnName("ownerId");
            entity.Property(e => e.SpecialPermissions)
                .HasColumnType("text")
                .HasColumnName("specialPermissions");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.VenueOwners)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VENUE_OWNER_USER");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
