using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public static partial class TournamentWorkflow
{
    public static readonly string[] PublicStatuses = ["Open", "Closed", "InProgress", "Completed"];
    public static readonly string[] TournamentStatuses =
        ["Draft", "PendingApproval", "Open", "Closed", "InProgress", "Completed", "Cancelled"];
    public static readonly string[] RegistrationReviewStatuses =
        ["Approved", "Waitlisted", "Rejected"];
    public static readonly string[] PaymentReviewStatuses = ["Confirmed", "Rejected"];

    public static bool CanRegister(Tournament tournament, DateTime utcNow) =>
        tournament.Status == "Open"
        && tournament.RegistrationDeadline > utcNow
        && DateOnly.FromDateTime(utcNow) <= tournament.StartDate;

    public static bool CanTransitionTournament(string current, string next) =>
        current == next
        || (current, next) switch
        {
            ("Draft", "PendingApproval" or "Cancelled") => true,
            ("PendingApproval", "Draft" or "Open" or "Cancelled") => true,
            ("Open", "Closed" or "InProgress" or "Cancelled") => true,
            ("Closed", "Open" or "InProgress" or "Cancelled") => true,
            ("InProgress", "Completed" or "Cancelled") => true,
            ("Completed", "InProgress") => true,
            _ => false
        };

    public static void EnsureCheckInCode(
        TournamentRegistration registration,
        Func<string>? codeFactory = null)
    {
        if (registration.Status != "Approved"
            || registration.PaymentStatus != "Confirmed"
            || !string.IsNullOrWhiteSpace(registration.CheckInCode))
        {
            return;
        }

        var suffix = codeFactory?.Invoke() ?? Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        registration.CheckInCode =
            $"PKL-T{registration.TournamentId}-R{registration.TournamentRegistrationId}-{suffix}";
    }

    public static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return MultipleDashesRegex()
            .Replace(InvalidSlugCharactersRegex().Replace(builder.ToString(), "-"), "-")
            .Trim('-');
    }

    public static string ToApiValue(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    public static TournamentSummaryResponse MapSummary(Tournament tournament) => new()
    {
        TournamentId = tournament.TournamentId,
        Slug = tournament.Slug,
        Name = tournament.Name,
        Status = ToApiValue(tournament.Status),
        ImageUrl = tournament.ImageUrl,
        City = tournament.City,
        VenueName = tournament.VenueName,
        StartDate = tournament.StartDate,
        EndDate = tournament.EndDate,
        RegistrationDeadline = AsUtc(tournament.RegistrationDeadline),
        Format = tournament.Format,
        SkillLevel = tournament.SkillLevel,
        Capacity = tournament.Capacity,
        RegisteredCount = tournament.Registrations.Count(item =>
            item.Status is "Pending" or "Approved" or "Waitlisted"),
        EntryFee = tournament.EntryFee,
        PrizePool = tournament.PrizePool,
        Description = tournament.Description
    };

    public static TournamentRegistrationResponse MapRegistration(TournamentRegistration registration) => new()
    {
        TournamentRegistrationId = registration.TournamentRegistrationId,
        TournamentId = registration.TournamentId,
        TournamentSlug = registration.Tournament.Slug,
        TournamentName = registration.Tournament.Name,
        TournamentImageUrl = registration.Tournament.ImageUrl,
        VenueName = registration.Tournament.VenueName,
        StartDate = registration.Tournament.StartDate,
        EndDate = registration.Tournament.EndDate,
        TournamentDivisionId = registration.TournamentDivisionId,
        DivisionName = registration.Division.Name,
        TeamName = registration.TeamName,
        PartnerName = registration.PartnerName,
        RepresentativePhone = registration.RepresentativePhone,
        Status = ToApiValue(registration.Status),
        PaymentStatus = ToApiValue(registration.PaymentStatus),
        AmountDue = registration.AmountDue,
        RegisteredAt = AsUtc(registration.RegisteredAt),
        RejectionReason = registration.RejectionReason,
        CheckInCode = registration.CheckInCode,
        CheckedInAt = registration.CheckedInAt is null ? null : AsUtc(registration.CheckedInAt.Value),
        Seed = registration.Seed,
        Payment = registration.Payment is null ? null : new TournamentPaymentResponse
        {
            TournamentPaymentId = registration.Payment.TournamentPaymentId,
            Amount = registration.Payment.Amount,
            PaymentMethod = registration.Payment.PaymentMethod,
            TransferContent = registration.Payment.TransferContent,
            ReceiptImageUrl = registration.Payment.ReceiptImageUrl,
            Status = ToApiValue(registration.Payment.Status),
            SubmittedAt = AsUtc(registration.Payment.SubmittedAt),
            VerifiedAt = registration.Payment.VerifiedAt is null
                ? null
                : AsUtc(registration.Payment.VerifiedAt.Value),
            RejectionReason = registration.Payment.RejectionReason
        }
    };

    public static TournamentMatchResponse MapMatch(TournamentMatch match, bool includeResult) => new()
    {
        TournamentMatchId = match.TournamentMatchId,
        TournamentDivisionId = match.TournamentDivisionId,
        DivisionName = match.Division.Name,
        RoundName = match.RoundName,
        MatchNumber = match.MatchNumber,
        Team1RegistrationId = match.Team1RegistrationId,
        Team1Name = match.Team1Registration?.TeamName,
        Team2RegistrationId = match.Team2RegistrationId,
        Team2Name = match.Team2Registration?.TeamName,
        ScheduledAt = match.ScheduledAt is null ? null : AsUtc(match.ScheduledAt.Value),
        CourtName = match.CourtName,
        Team1Score = includeResult ? match.Team1Score : null,
        Team2Score = includeResult ? match.Team2Score : null,
        WinnerRegistrationId = includeResult ? match.WinnerRegistrationId : null,
        WinnerName = includeResult ? match.WinnerRegistration?.TeamName : null,
        Status = includeResult || match.Status != "Completed" ? ToApiValue(match.Status) : "scheduled",
        Notes = includeResult ? match.Notes : null
    };

    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex InvalidSlugCharactersRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleDashesRegex();
}
