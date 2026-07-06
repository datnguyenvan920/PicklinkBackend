using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/tournaments")]
public class AdminTournamentsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminTournamentsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<TournamentAdminStatsResponse>> GetStats(
        CancellationToken cancellationToken)
    {
        var confirmedRevenue = await _dbContext.TournamentPayments.AsNoTracking()
            .Where(item => item.Status == "Confirmed")
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;

        return Ok(new TournamentAdminStatsResponse
        {
            TotalTournaments = await _dbContext.Tournaments.CountAsync(cancellationToken),
            PendingApproval = await _dbContext.Tournaments.CountAsync(
                item => item.Status == "PendingApproval",
                cancellationToken),
            OpenTournaments = await _dbContext.Tournaments.CountAsync(
                item => item.Status == "Open",
                cancellationToken),
            PendingRegistrations = await _dbContext.TournamentRegistrations.CountAsync(
                item => item.Status == "Pending",
                cancellationToken),
            PendingPayments = await _dbContext.TournamentPayments.CountAsync(
                item => item.Status == "Pending",
                cancellationToken),
            ConfirmedRevenue = confirmedRevenue
        });
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TournamentSummaryResponse>>> GetTournaments(
        string? search,
        string? status,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var query = _dbContext.Tournaments
            .AsNoTracking()
            .Include(item => item.Registrations)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(item =>
                item.Name.Contains(keyword)
                || item.OrganizerName.Contains(keyword)
                || item.VenueName.Contains(keyword)
                || item.City.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var normalizedStatus = NormalizeStatus(status, TournamentWorkflow.TournamentStatuses);
            if (normalizedStatus is null)
                return BadRequest(new { message = "Trạng thái giải đấu không hợp lệ." });
            query = query.Where(item => item.Status == normalizedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var tournaments = await query
            .OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(
            tournaments.Select(TournamentWorkflow.MapSummary),
            totalCount,
            page,
            pageSize));
    }

    [HttpGet("{tournamentId:int}")]
    public async Task<ActionResult<TournamentDetailResponse>> GetTournament(
        int tournamentId,
        CancellationToken cancellationToken)
    {
        var tournament = await TournamentDetailQuery()
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        return tournament is null
            ? NotFound(new { message = "Không tìm thấy giải đấu." })
            : Ok(TournamentsController.MapDetail(
                tournament,
                currentPlayerId: null,
                forceIncludeResults: true));
    }

    [HttpPost]
    public async Task<ActionResult<TournamentDetailResponse>> CreateTournament(
        CreateTournamentRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = ValidateTournamentRequest(request);
        if (validationResult is not null) return validationResult;

        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var slug = await CreateUniqueSlugAsync(
            string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug,
            excludedTournamentId: null,
            cancellationToken);
        var now = DateTime.UtcNow;
        var tournament = new Tournament
        {
            Slug = slug,
            Status = "Draft",
            CreatedByUserId = userId.Value,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyTournamentRequest(tournament, request);
        tournament.Divisions = request.Divisions
            .Select((item, index) => CreateDivision(item, index))
            .ToList();

        _dbContext.Tournaments.Add(tournament);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await TournamentDetailQuery()
            .SingleAsync(item => item.TournamentId == tournament.TournamentId, cancellationToken);
        return CreatedAtAction(
            nameof(GetTournament),
            new { tournamentId = tournament.TournamentId },
            TournamentsController.MapDetail(created, null, true));
    }

    [HttpPut("{tournamentId:int}")]
    public async Task<ActionResult<TournamentDetailResponse>> UpdateTournament(
        int tournamentId,
        CreateTournamentRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = ValidateTournamentRequest(request);
        if (validationResult is not null) return validationResult;

        var tournament = await _dbContext.Tournaments
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });

        ApplyTournamentRequest(tournament, request);
        tournament.Slug = await CreateUniqueSlugAsync(
            string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug,
            tournamentId,
            cancellationToken);
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await TournamentDetailQuery()
            .SingleAsync(item => item.TournamentId == tournamentId, cancellationToken);
        return Ok(TournamentsController.MapDetail(updated, null, true));
    }

    [HttpDelete("{tournamentId:int}")]
    public async Task<IActionResult> DeleteTournament(
        int tournamentId,
        CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments
            .Include(item => item.Registrations)
            .Include(item => item.Matches)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (tournament.Status != "Draft")
            return Conflict(new { message = "Chỉ được xóa giải ở trạng thái nháp." });
        if (tournament.Registrations.Count > 0 || tournament.Matches.Count > 0)
            return Conflict(new { message = "Giải đã phát sinh đăng ký hoặc lịch đấu nên không thể xóa." });

        _dbContext.Tournaments.Remove(tournament);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{tournamentId:int}/approve")]
    public async Task<ActionResult<TournamentDetailResponse>> ApproveTournament(
        int tournamentId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var tournament = await _dbContext.Tournaments
            .Include(item => item.Divisions)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (tournament.Status is not ("Draft" or "PendingApproval"))
            return Conflict(new { message = "Chỉ duyệt giải nháp hoặc đang chờ duyệt." });
        if (tournament.Divisions.Count == 0)
            return Conflict(new { message = "Cần tạo ít nhất một hạng mục trước khi duyệt giải." });
        if (tournament.RegistrationDeadline <= DateTime.UtcNow)
            return Conflict(new { message = "Hạn đăng ký phải nằm trong tương lai khi duyệt giải." });

        tournament.Status = "Open";
        tournament.ApprovedByUserId = userId.Value;
        tournament.ApprovedAt = DateTime.UtcNow;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await TournamentDetailQuery()
            .SingleAsync(item => item.TournamentId == tournamentId, cancellationToken);
        return Ok(TournamentsController.MapDetail(updated, null, true));
    }

    [HttpPut("{tournamentId:int}/status")]
    public async Task<ActionResult<TournamentDetailResponse>> UpdateTournamentStatus(
        int tournamentId,
        UpdateTournamentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var nextStatus = NormalizeStatus(request.Status, TournamentWorkflow.TournamentStatuses);
        if (nextStatus is null)
            return BadRequest(new { message = "Trạng thái giải đấu không hợp lệ." });

        var tournament = await _dbContext.Tournaments
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (!TournamentWorkflow.CanTransitionTournament(tournament.Status, nextStatus))
            return Conflict(new
            {
                message = $"Không thể chuyển giải từ {tournament.Status} sang {nextStatus}."
            });

        tournament.Status = nextStatus;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await TournamentDetailQuery()
            .SingleAsync(item => item.TournamentId == tournamentId, cancellationToken);
        return Ok(TournamentsController.MapDetail(updated, null, true));
    }

    [HttpPost("{tournamentId:int}/divisions")]
    public async Task<ActionResult<TournamentDivisionResponse>> CreateDivision(
        int tournamentId,
        UpsertTournamentDivisionRequest request,
        CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments
            .Include(item => item.Divisions)
            .Include(item => item.Registrations)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (tournament.Divisions.Any(item =>
            item.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "Tên hạng mục đã tồn tại trong giải." });

        var division = CreateDivision(request, tournament.Divisions.Count);
        tournament.Divisions.Add(division);
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapDivision(division, tournament));
    }

    [HttpPut("{tournamentId:int}/divisions/{divisionId:int}")]
    public async Task<ActionResult<TournamentDivisionResponse>> UpdateDivision(
        int tournamentId,
        int divisionId,
        UpsertTournamentDivisionRequest request,
        CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments
            .Include(item => item.Divisions)
            .Include(item => item.Registrations)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });

        var division = tournament.Divisions.SingleOrDefault(item =>
            item.TournamentDivisionId == divisionId);
        if (division is null)
            return NotFound(new { message = "Không tìm thấy hạng mục." });
        if (tournament.Divisions.Any(item =>
            item.TournamentDivisionId != divisionId
            && item.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "Tên hạng mục đã tồn tại trong giải." });

        division.Name = request.Name.Trim();
        division.Description = NormalizeOptional(request.Description);
        division.SkillLevel = NormalizeOptional(request.SkillLevel);
        division.Capacity = request.Capacity;
        division.EntryFee = request.EntryFee;
        division.DisplayOrder = request.DisplayOrder;
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapDivision(division, tournament));
    }

    [HttpDelete("{tournamentId:int}/divisions/{divisionId:int}")]
    public async Task<IActionResult> DeleteDivision(
        int tournamentId,
        int divisionId,
        CancellationToken cancellationToken)
    {
        var division = await _dbContext.TournamentDivisions
            .Include(item => item.Registrations)
            .Include(item => item.Matches)
            .SingleOrDefaultAsync(item =>
                item.TournamentId == tournamentId
                && item.TournamentDivisionId == divisionId,
                cancellationToken);
        if (division is null)
            return NotFound(new { message = "Không tìm thấy hạng mục." });
        if (division.Registrations.Count > 0 || division.Matches.Count > 0)
            return Conflict(new { message = "Hạng mục đã phát sinh đăng ký hoặc lịch đấu." });

        _dbContext.TournamentDivisions.Remove(division);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{tournamentId:int}/registrations")]
    public async Task<ActionResult<IReadOnlyList<TournamentRegistrationResponse>>> GetRegistrations(
        int tournamentId,
        string? status,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Tournaments.AsNoTracking()
            .AnyAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (!exists)
            return NotFound(new { message = "Không tìm thấy giải đấu." });

        var query = RegistrationQuery()
            .AsNoTracking()
            .Where(item => item.TournamentId == tournamentId);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var statuses = new[] { "Pending", "Approved", "Waitlisted", "Rejected", "Cancelled" };
            var normalizedStatus = NormalizeStatus(status, statuses);
            if (normalizedStatus is null)
                return BadRequest(new { message = "Trạng thái đăng ký không hợp lệ." });
            query = query.Where(item => item.Status == normalizedStatus);
        }

        var registrations = await query
            .OrderByDescending(item => item.RegisteredAt)
            .ToListAsync(cancellationToken);
        return Ok(registrations.Select(TournamentWorkflow.MapRegistration));
    }

    [HttpPut("registrations/{registrationId:int}/review")]
    public async Task<ActionResult<TournamentRegistrationResponse>> ReviewRegistration(
        int registrationId,
        ReviewTournamentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var nextStatus = NormalizeStatus(
            request.Status,
            TournamentWorkflow.RegistrationReviewStatuses);
        if (nextStatus is null)
            return BadRequest(new { message = "Quyết định duyệt đội không hợp lệ." });
        if (nextStatus == "Rejected" && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Cần nhập lý do từ chối đội." });

        var registration = await RegistrationQuery()
            .Include(item => item.Division.Registrations)
            .SingleOrDefaultAsync(
                item => item.TournamentRegistrationId == registrationId,
                cancellationToken);
        if (registration is null)
            return NotFound(new { message = "Không tìm thấy đăng ký." });
        if (registration.Status == "Cancelled")
            return Conflict(new { message = "Đăng ký đã bị người chơi hủy." });

        if (nextStatus == "Approved")
        {
            var approvedCount = registration.Division.Registrations.Count(item =>
                item.TournamentRegistrationId != registrationId
                && item.Status == "Approved");
            if (approvedCount >= registration.Division.Capacity)
                return Conflict(new { message = "Hạng mục đã đủ số đội được duyệt." });
        }

        registration.Status = nextStatus;
        registration.ReviewedAt = DateTime.UtcNow;
        registration.ReviewedByUserId = userId.Value;
        registration.RejectionReason = nextStatus == "Rejected"
            ? request.Reason!.Trim()
            : null;
        if (nextStatus != "Approved")
        {
            registration.CheckInCode = null;
        }
        else if (registration.AmountDue == 0)
        {
            registration.PaymentStatus = "Confirmed";
        }

        TournamentWorkflow.EnsureCheckInCode(registration);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(TournamentWorkflow.MapRegistration(registration));
    }

    [HttpGet("payments")]
    public async Task<ActionResult<IReadOnlyList<TournamentRegistrationResponse>>> GetPayments(
        string? status,
        int? tournamentId,
        CancellationToken cancellationToken)
    {
        var query = RegistrationQuery()
            .AsNoTracking()
            .Where(item => item.Payment != null);
        if (tournamentId is not null)
            query = query.Where(item => item.TournamentId == tournamentId.Value);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var normalizedStatus = NormalizeStatus(
                status,
                new[] { "Pending", "Confirmed", "Rejected" });
            if (normalizedStatus is null)
                return BadRequest(new { message = "Trạng thái đối soát không hợp lệ." });
            query = query.Where(item => item.Payment!.Status == normalizedStatus);
        }

        var registrations = await query
            .OrderByDescending(item => item.Payment!.SubmittedAt)
            .ToListAsync(cancellationToken);
        return Ok(registrations.Select(TournamentWorkflow.MapRegistration));
    }

    [HttpPut("payments/{paymentId:int}/review")]
    public async Task<ActionResult<TournamentRegistrationResponse>> ReviewPayment(
        int paymentId,
        ReviewTournamentPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var nextStatus = NormalizeStatus(
            request.Status,
            TournamentWorkflow.PaymentReviewStatuses);
        if (nextStatus is null)
            return BadRequest(new { message = "Quyết định đối soát không hợp lệ." });
        if (nextStatus == "Rejected" && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Cần nhập lý do từ chối thanh toán." });

        var registration = await RegistrationQuery()
            .SingleOrDefaultAsync(
                item => item.Payment != null
                    && item.Payment.TournamentPaymentId == paymentId,
                cancellationToken);
        if (registration?.Payment is null)
            return NotFound(new { message = "Không tìm thấy giao dịch lệ phí." });
        if (registration.Status != "Approved")
            return Conflict(new { message = "Đội chưa ở trạng thái được duyệt." });
        if (registration.CheckedInAt is not null && nextStatus == "Rejected")
            return Conflict(new { message = "Không thể từ chối lệ phí sau khi đội đã check-in." });

        registration.Payment.Status = nextStatus;
        registration.Payment.VerifiedAt = DateTime.UtcNow;
        registration.Payment.VerifiedByUserId = userId.Value;
        registration.Payment.RejectionReason = nextStatus == "Rejected"
            ? request.Reason!.Trim()
            : null;
        registration.PaymentStatus = nextStatus;
        if (nextStatus == "Rejected")
            registration.CheckInCode = null;
        TournamentWorkflow.EnsureCheckInCode(registration);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(TournamentWorkflow.MapRegistration(registration));
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<TournamentRegistrationResponse>> CheckIn(
        TournamentCheckInRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var normalizedCode = request.CheckInCode.Trim().ToUpperInvariant();

        var registration = await RegistrationQuery()
            .SingleOrDefaultAsync(
                item => item.CheckInCode == normalizedCode,
                cancellationToken);
        if (registration is null)
            return NotFound(new { message = "Mã check-in không hợp lệ." });
        if (registration.Status != "Approved" || registration.PaymentStatus != "Confirmed")
            return Conflict(new { message = "Đội chưa đủ điều kiện check-in." });
        if (registration.CheckedInAt is not null)
            return Conflict(new
            {
                message = $"Đội đã check-in lúc {registration.CheckedInAt:dd/MM/yyyy HH:mm}."
            });

        registration.CheckedInAt = DateTime.UtcNow;
        registration.CheckedInByUserId = userId.Value;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(TournamentWorkflow.MapRegistration(registration));
    }

    [HttpPost("{tournamentId:int}/matches")]
    public async Task<ActionResult<TournamentMatchResponse>> CreateMatch(
        int tournamentId,
        UpsertTournamentMatchRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await ValidateMatchRequestAsync(
            tournamentId,
            matchId: null,
            request,
            cancellationToken);
        if (validationResult is not null) return validationResult;

        var now = DateTime.UtcNow;
        var match = new TournamentMatch
        {
            TournamentId = tournamentId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = "Scheduled"
        };
        ApplyMatchRequest(match, request);
        _dbContext.TournamentMatches.Add(match);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await MatchQuery()
            .AsNoTracking()
            .SingleAsync(item => item.TournamentMatchId == match.TournamentMatchId, cancellationToken);
        return Ok(TournamentWorkflow.MapMatch(created, includeResult: true));
    }

    [HttpPut("{tournamentId:int}/matches/{matchId:int}")]
    public async Task<ActionResult<TournamentMatchResponse>> UpdateMatch(
        int tournamentId,
        int matchId,
        UpsertTournamentMatchRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await ValidateMatchRequestAsync(
            tournamentId,
            matchId,
            request,
            cancellationToken);
        if (validationResult is not null) return validationResult;

        var match = await _dbContext.TournamentMatches.SingleAsync(
            item => item.TournamentId == tournamentId
                && item.TournamentMatchId == matchId,
            cancellationToken);
        if (match.Status == "Completed")
            return Conflict(new { message = "Không sửa lịch của trận đã nhập kết quả." });

        ApplyMatchRequest(match, request);
        match.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await MatchQuery()
            .AsNoTracking()
            .SingleAsync(item => item.TournamentMatchId == matchId, cancellationToken);
        return Ok(TournamentWorkflow.MapMatch(updated, includeResult: true));
    }

    [HttpDelete("{tournamentId:int}/matches/{matchId:int}")]
    public async Task<IActionResult> DeleteMatch(
        int tournamentId,
        int matchId,
        CancellationToken cancellationToken)
    {
        var match = await _dbContext.TournamentMatches.SingleOrDefaultAsync(
            item => item.TournamentId == tournamentId
                && item.TournamentMatchId == matchId,
            cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy trận đấu." });
        if (match.Status == "Completed")
            return Conflict(new { message = "Không xóa trận đã nhập kết quả." });

        _dbContext.TournamentMatches.Remove(match);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{tournamentId:int}/matches/{matchId:int}/result")]
    public async Task<ActionResult<TournamentMatchResponse>> RecordResult(
        int tournamentId,
        int matchId,
        RecordTournamentResultRequest request,
        CancellationToken cancellationToken)
    {
        var match = await MatchQuery().SingleOrDefaultAsync(
            item => item.TournamentId == tournamentId
                && item.TournamentMatchId == matchId,
            cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy trận đấu." });
        if (match.Team1RegistrationId is null || match.Team2RegistrationId is null)
            return Conflict(new { message = "Cần xếp đủ hai đội trước khi nhập kết quả." });
        if (request.Team1Score == request.Team2Score)
            return BadRequest(new { message = "Kết quả trận không được hòa." });
        if (request.WinnerRegistrationId != match.Team1RegistrationId
            && request.WinnerRegistrationId != match.Team2RegistrationId)
            return BadRequest(new { message = "Đội thắng không thuộc trận đấu." });

        var expectedWinnerId = request.Team1Score > request.Team2Score
            ? match.Team1RegistrationId.Value
            : match.Team2RegistrationId.Value;
        if (request.WinnerRegistrationId != expectedWinnerId)
            return BadRequest(new { message = "Đội thắng không khớp với tỷ số." });

        match.Team1Score = request.Team1Score;
        match.Team2Score = request.Team2Score;
        match.WinnerRegistrationId = expectedWinnerId;
        match.Status = "Completed";
        match.Notes = NormalizeOptional(request.Notes);
        match.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await MatchQuery().AsNoTracking()
            .SingleAsync(item => item.TournamentMatchId == matchId, cancellationToken);
        return Ok(TournamentWorkflow.MapMatch(updated, includeResult: true));
    }

    [HttpPost("{tournamentId:int}/publish-results")]
    public async Task<ActionResult<TournamentDetailResponse>> PublishResults(
        int tournamentId,
        CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments
            .Include(item => item.Matches)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (tournament.Matches.Count == 0)
            return Conflict(new { message = "Giải chưa có trận đấu để công bố." });
        if (tournament.Matches.Any(item =>
            item.Team1RegistrationId is not null
            && item.Team2RegistrationId is not null
            && item.Status != "Completed"))
            return Conflict(new { message = "Còn trận đã xếp đội nhưng chưa nhập kết quả." });

        tournament.ResultsPublishedAt = DateTime.UtcNow;
        tournament.Status = "Completed";
        tournament.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await TournamentDetailQuery()
            .SingleAsync(item => item.TournamentId == tournamentId, cancellationToken);
        return Ok(TournamentsController.MapDetail(updated, null, true));
    }

    private IQueryable<Tournament> TournamentDetailQuery() =>
        _dbContext.Tournaments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Divisions)
            .Include(item => item.Registrations)
                .ThenInclude(item => item.CaptainPlayer)
                .ThenInclude(item => item.User)
            .Include(item => item.Registrations)
                .ThenInclude(item => item.Division)
            .Include(item => item.Registrations)
                .ThenInclude(item => item.Payment)
            .Include(item => item.Matches)
                .ThenInclude(item => item.Division)
            .Include(item => item.Matches)
                .ThenInclude(item => item.Team1Registration)
            .Include(item => item.Matches)
                .ThenInclude(item => item.Team2Registration)
            .Include(item => item.Matches)
                .ThenInclude(item => item.WinnerRegistration);

    private IQueryable<TournamentRegistration> RegistrationQuery() =>
        _dbContext.TournamentRegistrations
            .AsSplitQuery()
            .Include(item => item.Tournament)
            .Include(item => item.Division)
            .Include(item => item.Payment);

    private IQueryable<TournamentMatch> MatchQuery() =>
        _dbContext.TournamentMatches
            .Include(item => item.Division)
            .Include(item => item.Team1Registration)
            .Include(item => item.Team2Registration)
            .Include(item => item.WinnerRegistration);

    private static void ApplyTournamentRequest(
        Tournament tournament,
        CreateTournamentRequest request)
    {
        tournament.Name = request.Name.Trim();
        tournament.Description = NormalizeOptional(request.Description);
        tournament.Rules = NormalizeOptional(request.Rules);
        tournament.ImageUrl = NormalizeOptional(request.ImageUrl);
        tournament.VenueName = request.VenueName.Trim();
        tournament.Address = request.Address.Trim();
        tournament.City = request.City.Trim();
        tournament.OrganizerName = request.OrganizerName.Trim();
        tournament.OrganizerPhone = NormalizeOptional(request.OrganizerPhone);
        tournament.Format = request.Format.Trim();
        tournament.BracketType = request.BracketType.Trim();
        tournament.SkillLevel = NormalizeOptional(request.SkillLevel);
        tournament.StartDate = request.StartDate;
        tournament.EndDate = request.EndDate;
        tournament.RegistrationDeadline = request.RegistrationDeadline.ToUniversalTime();
        tournament.Capacity = request.Capacity;
        tournament.EntryFee = request.EntryFee;
        tournament.PrizePool = request.PrizePool;
    }

    private static TournamentDivision CreateDivision(
        UpsertTournamentDivisionRequest request,
        int fallbackOrder) => new()
        {
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            SkillLevel = NormalizeOptional(request.SkillLevel),
            Capacity = request.Capacity,
            EntryFee = request.EntryFee,
            Status = "Open",
            DisplayOrder = request.DisplayOrder == 0 ? fallbackOrder : request.DisplayOrder
        };

    private static TournamentDivisionResponse MapDivision(
        TournamentDivision division,
        Tournament tournament) => new()
        {
            TournamentDivisionId = division.TournamentDivisionId,
            Name = division.Name,
            Description = division.Description,
            SkillLevel = division.SkillLevel,
            Capacity = division.Capacity,
            RegisteredCount = tournament.Registrations.Count(item =>
                item.TournamentDivisionId == division.TournamentDivisionId
                && item.Status is "Pending" or "Approved" or "Waitlisted"),
            EntryFee = division.EntryFee ?? tournament.EntryFee,
            Status = TournamentWorkflow.ToApiValue(division.Status),
            DisplayOrder = division.DisplayOrder
        };

    private static void ApplyMatchRequest(
        TournamentMatch match,
        UpsertTournamentMatchRequest request)
    {
        match.TournamentDivisionId = request.TournamentDivisionId;
        match.RoundName = request.RoundName.Trim();
        match.MatchNumber = request.MatchNumber;
        match.Team1RegistrationId = request.Team1RegistrationId;
        match.Team2RegistrationId = request.Team2RegistrationId;
        match.ScheduledAt = request.ScheduledAt?.ToUniversalTime();
        match.CourtName = NormalizeOptional(request.CourtName);
        match.Notes = NormalizeOptional(request.Notes);
    }

    private async Task<ActionResult?> ValidateMatchRequestAsync(
        int tournamentId,
        int? matchId,
        UpsertTournamentMatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Team1RegistrationId is not null
            && request.Team1RegistrationId == request.Team2RegistrationId)
            return BadRequest(new { message = "Hai vị trí thi đấu phải là hai đội khác nhau." });

        var divisionExists = await _dbContext.TournamentDivisions.AsNoTracking()
            .AnyAsync(item =>
                item.TournamentId == tournamentId
                && item.TournamentDivisionId == request.TournamentDivisionId,
                cancellationToken);
        if (!divisionExists)
            return BadRequest(new { message = "Hạng mục không thuộc giải đấu." });

        var duplicateRound = await _dbContext.TournamentMatches.AsNoTracking()
            .AnyAsync(item =>
                item.TournamentDivisionId == request.TournamentDivisionId
                && item.RoundName == request.RoundName.Trim()
                && item.MatchNumber == request.MatchNumber
                && item.TournamentMatchId != matchId,
                cancellationToken);
        if (duplicateRound)
            return Conflict(new { message = "Số trận đã tồn tại trong vòng đấu này." });

        var registrationIds = new[]
            {
                request.Team1RegistrationId,
                request.Team2RegistrationId
            }
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .ToList();
        if (registrationIds.Count == 0) return null;

        var validRegistrationCount = await _dbContext.TournamentRegistrations.AsNoTracking()
            .CountAsync(item =>
                registrationIds.Contains(item.TournamentRegistrationId)
                && item.TournamentId == tournamentId
                && item.TournamentDivisionId == request.TournamentDivisionId
                && item.Status == "Approved",
                cancellationToken);
        return validRegistrationCount == registrationIds.Count
            ? null
            : BadRequest(new
            {
                message = "Đội thi đấu phải được duyệt và thuộc đúng hạng mục."
            });
    }

    private ActionResult? ValidateTournamentRequest(CreateTournamentRequest request)
    {
        if (request.StartDate == default || request.EndDate == default)
            return BadRequest(new { message = "Cần nhập đầy đủ ngày bắt đầu và kết thúc." });
        if (request.EndDate < request.StartDate)
            return BadRequest(new { message = "Ngày kết thúc phải từ ngày bắt đầu trở đi." });
        if (request.RegistrationDeadline.Date >= request.StartDate.ToDateTime(TimeOnly.MinValue))
            return BadRequest(new { message = "Hạn đăng ký phải trước ngày bắt đầu giải." });
        if (request.Divisions
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            return BadRequest(new { message = "Tên hạng mục trong giải không được trùng nhau." });
        return null;
    }

    private async Task<string> CreateUniqueSlugAsync(
        string source,
        int? excludedTournamentId,
        CancellationToken cancellationToken)
    {
        var baseSlug = TournamentWorkflow.Slugify(source);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "giai-dau";
        var candidate = baseSlug;
        var suffix = 2;
        while (await _dbContext.Tournaments.AsNoTracking().AnyAsync(
            item => item.Slug == candidate
                && item.TournamentId != excludedTournamentId,
            cancellationToken))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }
        return candidate;
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string? NormalizeStatus(string value, IEnumerable<string> allowed) =>
        allowed.FirstOrDefault(item => item.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
