using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Tournament
{
    public int TournamentId { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? Description { get; set; }

    public string? Rules { get; set; }

    public string? ImageUrl { get; set; }

    public string VenueName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string City { get; set; } = null!;

    public string OrganizerName { get; set; } = null!;

    public string? OrganizerPhone { get; set; }

    public string Format { get; set; } = null!;

    public string BracketType { get; set; } = null!;

    public string? SkillLevel { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public DateTime RegistrationDeadline { get; set; }

    public int Capacity { get; set; }

    public decimal EntryFee { get; set; }

    public decimal PrizePool { get; set; }

    public string Status { get; set; } = null!;

    public int CreatedByUserId { get; set; }

    public int? ApprovedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? ResultsPublishedAt { get; set; }

    public virtual ICollection<TournamentDivision> Divisions { get; set; } = new List<TournamentDivision>();

    public virtual ICollection<TournamentRegistration> Registrations { get; set; } = new List<TournamentRegistration>();

    public virtual ICollection<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
