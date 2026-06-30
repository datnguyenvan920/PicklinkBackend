namespace PicklinkBackend.Models;

public class TournamentRegistration
{
    public int TournamentRegistrationId { get; set; }

    public int TournamentId { get; set; }

    public int TournamentDivisionId { get; set; }

    public int CaptainPlayerId { get; set; }

    public string TeamName { get; set; } = null!;

    public string? PartnerName { get; set; }

    public string RepresentativePhone { get; set; } = null!;

    public string Status { get; set; } = "Pending";

    public string PaymentStatus { get; set; } = "Unpaid";

    public decimal AmountDue { get; set; }

    public DateTime RegisteredAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public string? RejectionReason { get; set; }

    public string? CheckInCode { get; set; }

    public DateTime? CheckedInAt { get; set; }

    public int? CheckedInByUserId { get; set; }

    public int? Seed { get; set; }

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual TournamentDivision Division { get; set; } = null!;

    public virtual Player CaptainPlayer { get; set; } = null!;

    public virtual TournamentPayment? Payment { get; set; }

    public virtual ICollection<TournamentMatch> Team1Matches { get; set; } = new List<TournamentMatch>();

    public virtual ICollection<TournamentMatch> Team2Matches { get; set; } = new List<TournamentMatch>();

    public virtual ICollection<TournamentMatch> WonMatches { get; set; } = new List<TournamentMatch>();
}
