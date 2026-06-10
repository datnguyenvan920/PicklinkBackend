using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using PickLink.Domain.Entities;

public class PickLinkDbContext : DbContext
{
    public PickLinkDbContext(DbContextOptions<PickLinkDbContext> options) : base(options) { }

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<VenueOwner> VenueOwners => Set<VenueOwner>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<MarketplaceProvider> MarketplaceProviders => Set<MarketplaceProvider>();

    // Venue
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<BookingRules> BookingRules => Set<BookingRules>();
    public DbSet<VenueAuditLog> VenueAuditLogs => Set<VenueAuditLog>();

    // Match & Tournament
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<PlayerTeamRoster> PlayerTeamRosters => Set<PlayerTeamRoster>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<MatchCheckin> MatchCheckins => Set<MatchCheckin>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentTeam> TournamentTeams => Set<TournamentTeam>();
    public DbSet<Scorecard> Scorecards => Set<Scorecard>();

    // Booking & Payment
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Marketplace
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    // Analytics & Logs
    public DbSet<RatingHistory> RatingHistories => Set<RatingHistory>();
    public DbSet<SkillMatchup> SkillMatchups => Set<SkillMatchup>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    // Social
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMediaItems => Set<PostMedia>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<PostReport> PostReports => Set<PostReport>();
    public DbSet<SocialGroup> SocialGroups => Set<SocialGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);

        // USER
        m.Entity<User>(e => {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.UserType).HasConversion<string>().HasMaxLength(50);
        });

        // PLAYER
        m.Entity<Player>(e => {
            e.Property(p => p.Prestige).HasDefaultValue(100); // BR-14
            e.HasOne(p => p.User).WithOne(u => u.Player)
             .HasForeignKey<Player>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // VENUE OWNER
        m.Entity<VenueOwner>(e =>
            e.HasOne(v => v.User).WithOne(u => u.VenueOwner)
             .HasForeignKey<VenueOwner>(v => v.UserId).OnDelete(DeleteBehavior.Restrict));

        // STAFF
        m.Entity<Staff>(e =>
            e.HasOne(s => s.User).WithOne(u => u.Staff)
             .HasForeignKey<Staff>(s => s.UserId).OnDelete(DeleteBehavior.Restrict));

        // VENUE
        m.Entity<Venue>(e => {
            e.Property(v => v.OverallRating).HasPrecision(3, 2);
            e.Property(v => v.Latitude).HasPrecision(9, 6);
            e.Property(v => v.Longitude).HasPrecision(9, 6);
            e.HasOne(v => v.Owner).WithMany(o => o.Venues)
             .HasForeignKey(v => v.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        // TEAM
        m.Entity<Team>(e =>
            e.HasOne(t => t.Captain).WithMany(p => p.CaptainedTeams)
             .HasForeignKey(t => t.CaptainId).OnDelete(DeleteBehavior.Restrict));

        // PLAYER TEAM ROSTER (composite PK)
        m.Entity<PlayerTeamRoster>(e => e.HasKey(r => new { r.PlayerId, r.TeamId }));

        // MATCH
        m.Entity<Match>(e => {
            e.ToTable("Match");
            e.Property(x => x.MatchType).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
            e.HasOne(x => x.Team1).WithMany(t => t.Team1Matches)
             .HasForeignKey(x => x.Team1Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Team2).WithMany(t => t.Team2Matches)
             .HasForeignKey(x => x.Team2Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WinningTeam).WithMany()
             .HasForeignKey(x => x.WinningTeamId).OnDelete(DeleteBehavior.Restrict);
        });

        // MATCH PARTICIPANT — unique per match+player
        m.Entity<MatchParticipant>(e =>
            e.HasIndex(mp => new { mp.MatchId, mp.PlayerId }).IsUnique());

        // MATCH CHECKIN — DC-02: no double check-in
        m.Entity<MatchCheckin>(e =>
            e.HasIndex(c => new { c.MatchId, c.PlayerId }).IsUnique());

        // TOURNAMENT
        m.Entity<Tournament>(e =>
            e.HasOne(t => t.Organizer).WithMany()
             .HasForeignKey(t => t.OrganizerId).OnDelete(DeleteBehavior.Restrict));

        // TOURNAMENT TEAM (composite PK)
        m.Entity<TournamentTeam>(e => e.HasKey(tt => new { tt.TournamentId, tt.TeamId }));

        // BOOKING — DC-01: EndTime > StartTime
        m.Entity<Booking>(e => {
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(50);
            e.ToTable(t => t.HasCheckConstraint("CHK_Booking_Times", "EndTime > StartTime"));
            e.HasOne(b => b.Player).WithMany(p => p.Bookings)
             .HasForeignKey(b => b.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        // PAYMENT — financial precision
        m.Entity<Payment>(e => {
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(50);
            e.HasOne(p => p.Payer).WithMany(pl => pl.Payments)
             .HasForeignKey(p => p.PayerId).OnDelete(DeleteBehavior.Restrict);
        });

        // RATING HISTORY — DC-03: score 1–5
        m.Entity<RatingHistory>(e =>
            e.ToTable(t => t.HasCheckConstraint("CHK_Rating_Score", "Score BETWEEN 1 AND 5")));

        // FRIENDSHIP — DC-05: no self-friendship
        m.Entity<Friendship>(e => {
            e.Property(f => f.Status).HasConversion<string>().HasMaxLength(50);
            e.HasIndex(f => new { f.RequesterId, f.ReceiverId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CHK_Friendship_NoSelf", "RequesterId <> ReceiverId"));
            e.HasOne(f => f.Requester).WithMany(u => u.SentFriendships)
             .HasForeignKey(f => f.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Receiver).WithMany(u => u.ReceivedFriendships)
             .HasForeignKey(f => f.ReceiverId).OnDelete(DeleteBehavior.Restrict);
        });

        // POST
        m.Entity<Post>(e =>
            e.Property(p => p.Visibility).HasConversion<string>().HasMaxLength(50));

        // POST LIKE — DC-04: one reaction per user per post
        m.Entity<PostLike>(e =>
            e.HasIndex(pl => new { pl.PostId, pl.UserId }).IsUnique());

        // POST COMMENT — self-referencing reply tree
        m.Entity<PostComment>(e =>
            e.HasOne(c => c.ParentComment).WithMany(c => c.Replies)
             .HasForeignKey(c => c.ParentCommentId).OnDelete(DeleteBehavior.ClientSetNull));

        // POST REPORT — one report per user per post
        m.Entity<PostReport>(e =>
            e.HasIndex(r => new { r.PostId, r.ReporterId }).IsUnique());

        // SOCIAL GROUP
        m.Entity<SocialGroup>(e =>
            e.HasOne(g => g.Owner).WithMany(p => p.OwnedGroups)
             .HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Restrict));

        // GROUP MEMBER (composite PK)
        m.Entity<GroupMember>(e => e.HasKey(gm => new { gm.GroupId, gm.UserId }));

        // CONVERSATION PARTICIPANT (composite PK)
        m.Entity<ConversationParticipant>(e => e.HasKey(cp => new { cp.ConversationId, cp.UserId }));

        // MESSAGE — self-referencing reply
        m.Entity<Message>(e =>
            e.HasOne(msg => msg.ReplyToMessage).WithMany()
             .HasForeignKey(msg => msg.ReplyToMessageId).OnDelete(DeleteBehavior.ClientSetNull));
    }
}
