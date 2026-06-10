using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string UserType { get; set; } = null!;

    public string? ProfileImageUrl { get; set; }

    public string? City { get; set; }

    public virtual ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();

    public virtual ICollection<Friendship> FriendshipReceivers { get; set; } = new List<Friendship>();

    public virtual ICollection<Friendship> FriendshipRequesters { get; set; } = new List<Friendship>();

    public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();

    public virtual ICollection<MarketplaceProvider> MarketplaceProviders { get; set; } = new List<MarketplaceProvider>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();

    public virtual ICollection<Player> Players { get; set; } = new List<Player>();

    public virtual ICollection<PostComment> PostComments { get; set; } = new List<PostComment>();

    public virtual ICollection<PostLike> PostLikes { get; set; } = new List<PostLike>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<RatingHistory> RatingHistories { get; set; } = new List<RatingHistory>();

    public virtual ICollection<Staff> Staff { get; set; } = new List<Staff>();

    public virtual ICollection<VenueAuditLog> VenueAuditLogs { get; set; } = new List<VenueAuditLog>();

    public virtual ICollection<VenueOwner> VenueOwners { get; set; } = new List<VenueOwner>();
}
