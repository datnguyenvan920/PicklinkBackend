namespace PicklinkBackend.Models;

public class TournamentMatch
{
    public int TournamentMatchId { get; set; }

    public int TournamentId { get; set; }

    public int TournamentDivisionId { get; set; }

    public string RoundName { get; set; } = null!;

    public int MatchNumber { get; set; }

    public int? Team1RegistrationId { get; set; }

    public int? Team2RegistrationId { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public string? CourtName { get; set; }

    public int? Team1Score { get; set; }

    public int? Team2Score { get; set; }

    public int? WinnerRegistrationId { get; set; }

    public string Status { get; set; } = "Scheduled";

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual TournamentDivision Division { get; set; } = null!;

    public virtual TournamentRegistration? Team1Registration { get; set; }

    public virtual TournamentRegistration? Team2Registration { get; set; }

    public virtual TournamentRegistration? WinnerRegistration { get; set; }
}
