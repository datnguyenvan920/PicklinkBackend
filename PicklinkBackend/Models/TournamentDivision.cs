namespace PicklinkBackend.Models;

public class TournamentDivision
{
    public int TournamentDivisionId { get; set; }

    public int TournamentId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? SkillLevel { get; set; }

    public int Capacity { get; set; }

    public decimal? EntryFee { get; set; }

    public string Status { get; set; } = "Open";

    public int DisplayOrder { get; set; }

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual ICollection<TournamentRegistration> Registrations { get; set; } = new List<TournamentRegistration>();

    public virtual ICollection<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
}
