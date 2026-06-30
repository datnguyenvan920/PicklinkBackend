using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class TournamentSummaryResponse
{
    public int TournamentId { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string City { get; set; } = "";
    public string VenueName { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime RegistrationDeadline { get; set; }
    public string Format { get; set; } = "";
    public string? SkillLevel { get; set; }
    public int Capacity { get; set; }
    public int RegisteredCount { get; set; }
    public decimal EntryFee { get; set; }
    public decimal PrizePool { get; set; }
    public string? Description { get; set; }
}

public class TournamentDetailResponse : TournamentSummaryResponse
{
    public string Address { get; set; } = "";
    public string OrganizerName { get; set; } = "";
    public string? OrganizerPhone { get; set; }
    public string BracketType { get; set; } = "";
    public IReadOnlyList<string> Rules { get; set; } = [];
    public IReadOnlyList<TournamentDivisionResponse> Divisions { get; set; } = [];
    public IReadOnlyList<TournamentTeamResponse> Teams { get; set; } = [];
    public IReadOnlyList<TournamentMatchResponse> Matches { get; set; } = [];
    public TournamentRegistrationResponse? MyRegistration { get; set; }
    public DateTime? ResultsPublishedAt { get; set; }
}

public class TournamentDivisionResponse
{
    public int TournamentDivisionId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? SkillLevel { get; set; }
    public int Capacity { get; set; }
    public int RegisteredCount { get; set; }
    public decimal EntryFee { get; set; }
    public string Status { get; set; } = "";
    public int DisplayOrder { get; set; }
}

public class TournamentTeamResponse
{
    public int RegistrationId { get; set; }
    public string TeamName { get; set; } = "";
    public string DivisionName { get; set; } = "";
    public string? Area { get; set; }
    public string? SkillLevel { get; set; }
    public string Status { get; set; } = "";
}

public class TournamentRegistrationResponse
{
    public int TournamentRegistrationId { get; set; }
    public int TournamentId { get; set; }
    public string TournamentSlug { get; set; } = "";
    public string TournamentName { get; set; } = "";
    public string? TournamentImageUrl { get; set; }
    public string VenueName { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int TournamentDivisionId { get; set; }
    public string DivisionName { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string? PartnerName { get; set; }
    public string RepresentativePhone { get; set; } = "";
    public string Status { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
    public decimal AmountDue { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? CheckInCode { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? Seed { get; set; }
    public TournamentPaymentResponse? Payment { get; set; }
}

public class TournamentPaymentResponse
{
    public int TournamentPaymentId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string? TransferContent { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public string Status { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public class TournamentMatchResponse
{
    public int TournamentMatchId { get; set; }
    public int TournamentDivisionId { get; set; }
    public string DivisionName { get; set; } = "";
    public string RoundName { get; set; } = "";
    public int MatchNumber { get; set; }
    public int? Team1RegistrationId { get; set; }
    public string? Team1Name { get; set; }
    public int? Team2RegistrationId { get; set; }
    public string? Team2Name { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? CourtName { get; set; }
    public int? Team1Score { get; set; }
    public int? Team2Score { get; set; }
    public int? WinnerRegistrationId { get; set; }
    public string? WinnerName { get; set; }
    public string Status { get; set; } = "";
    public string? Notes { get; set; }
}

public class CreateTournamentRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Name { get; set; } = "";

    [StringLength(220)]
    public string? Slug { get; set; }

    [StringLength(5000)]
    public string? Description { get; set; }

    [StringLength(20000)]
    public string? Rules { get; set; }

    [Url, StringLength(1000)]
    public string? ImageUrl { get; set; }

    [Required, StringLength(200)]
    public string VenueName { get; set; } = "";

    [Required, StringLength(500)]
    public string Address { get; set; } = "";

    [Required, StringLength(100)]
    public string City { get; set; } = "";

    [Required, StringLength(200)]
    public string OrganizerName { get; set; } = "";

    [Phone, StringLength(30)]
    public string? OrganizerPhone { get; set; }

    [Required, StringLength(100)]
    public string Format { get; set; } = "";

    [Required, StringLength(100)]
    public string BracketType { get; set; } = "";

    [StringLength(100)]
    public string? SkillLevel { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime RegistrationDeadline { get; set; }

    [Range(1, 10000)]
    public int Capacity { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal EntryFee { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal PrizePool { get; set; }

    public IReadOnlyList<UpsertTournamentDivisionRequest> Divisions { get; set; } = [];
}

public class UpsertTournamentDivisionRequest
{
    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(100)]
    public string? SkillLevel { get; set; }

    [Range(1, 10000)]
    public int Capacity { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal? EntryFee { get; set; }

    public int DisplayOrder { get; set; }
}

public class UpdateTournamentStatusRequest
{
    [Required]
    public string Status { get; set; } = "";
}

public class CreateTournamentRegistrationRequest
{
    [Range(1, int.MaxValue)]
    public int TournamentDivisionId { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string TeamName { get; set; } = "";

    [StringLength(200)]
    public string? PartnerName { get; set; }

    [Required, Phone, StringLength(30)]
    public string RepresentativePhone { get; set; } = "";
}

public class SubmitTournamentPaymentRequest
{
    [Required, StringLength(50)]
    public string PaymentMethod { get; set; } = "BankTransfer";

    [StringLength(250)]
    public string? TransferContent { get; set; }

    [Url, StringLength(1000)]
    public string? ReceiptImageUrl { get; set; }
}

public class SubmitTournamentPaymentReceiptRequest
{
    [Required]
    public IFormFile? Receipt { get; set; }

    [StringLength(250)]
    public string? TransferContent { get; set; }
}

public class ReviewTournamentRegistrationRequest
{
    [Required]
    public string Status { get; set; } = "";

    [StringLength(500)]
    public string? Reason { get; set; }
}

public class ReviewTournamentPaymentRequest
{
    [Required]
    public string Status { get; set; } = "";

    [StringLength(500)]
    public string? Reason { get; set; }
}

public class TournamentCheckInRequest
{
    [Required, StringLength(40)]
    public string CheckInCode { get; set; } = "";
}

public class UpsertTournamentMatchRequest
{
    [Range(1, int.MaxValue)]
    public int TournamentDivisionId { get; set; }

    [Required, StringLength(100)]
    public string RoundName { get; set; } = "";

    [Range(1, 10000)]
    public int MatchNumber { get; set; }

    public int? Team1RegistrationId { get; set; }
    public int? Team2RegistrationId { get; set; }
    public DateTime? ScheduledAt { get; set; }

    [StringLength(100)]
    public string? CourtName { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class RecordTournamentResultRequest
{
    [Range(0, 1000)]
    public int Team1Score { get; set; }

    [Range(0, 1000)]
    public int Team2Score { get; set; }

    [Range(1, int.MaxValue)]
    public int WinnerRegistrationId { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class TournamentAdminStatsResponse
{
    public int TotalTournaments { get; set; }
    public int PendingApproval { get; set; }
    public int OpenTournaments { get; set; }
    public int PendingRegistrations { get; set; }
    public int PendingPayments { get; set; }
    public decimal ConfirmedRevenue { get; set; }
}
