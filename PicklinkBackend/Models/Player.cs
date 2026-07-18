using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Player
{
    public int PlayerId { get; set; }

    public int UserId { get; set; }

    public int Prestige { get; set; }

    public double SkillLevel { get; set; }

    public string? PlayerSubType { get; set; }

    public string? PlayFrequency { get; set; }

    public string? PreferredTimeSlot { get; set; }

    public string? Bio { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? Gender { get; set; }

    public double? HeightCm { get; set; }

    public double? WeightKg { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<FavoriteVenue> FavoriteVenues { get; set; } = new List<FavoriteVenue>();

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();

    public virtual ICollection<Match> HostedMatches { get; set; } = new List<Match>();

    public virtual ICollection<MatchPlayerReview> MatchReviewsReceived { get; set; } = new List<MatchPlayerReview>();

    public virtual ICollection<MatchPlayerReview> MatchReviewsWritten { get; set; } = new List<MatchPlayerReview>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<SessionTicket> SessionTickets { get; set; } = new List<SessionTicket>();

    public virtual ICollection<PlayerTeamRoster> PlayerTeamRosters { get; set; } = new List<PlayerTeamRoster>();

    public virtual ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();

    public virtual ICollection<SocialGroup> SocialGroups { get; set; } = new List<SocialGroup>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual ICollection<TournamentRegistration> TournamentRegistrations { get; set; } = new List<TournamentRegistration>();

    public virtual User User { get; set; } = null!;
}
