using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;
using System.Numerics;

public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserType UserType { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? City { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsPhoneVerified { get; set; } = false; // BR-08

    public Player? Player { get; set; }
    public VenueOwner? VenueOwner { get; set; }
    public Staff? Staff { get; set; }
    public MarketplaceProvider? MarketplaceProvider { get; set; }
    public ICollection<RatingHistory> RatingHistories { get; set; } = new List<RatingHistory>();
    public ICollection<NotificationLog> Notifications { get; set; } = new List<NotificationLog>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Friendship> SentFriendships { get; set; } = new List<Friendship>();
    public ICollection<Friendship> ReceivedFriendships { get; set; } = new List<Friendship>();
}