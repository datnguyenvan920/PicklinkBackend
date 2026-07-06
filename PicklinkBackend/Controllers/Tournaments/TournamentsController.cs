using System.Data;
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
[Route("api/tournaments")]
public class TournamentsController : ControllerBase
{
    private static readonly HashSet<string> AllowedReceiptTypes =
        ["image/jpeg", "image/png", "image/webp"];
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public TournamentsController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TournamentSummaryResponse>>> GetTournaments(
        string? search,
        string? city,
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
            .Where(item => TournamentWorkflow.PublicStatuses.Contains(item.Status));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(item =>
                item.Name.Contains(keyword)
                || item.OrganizerName.Contains(keyword)
                || item.VenueName.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(city) && !city.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedCity = city.Trim();
            query = query.Where(item => item.City == normalizedCity);
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedStatus = NormalizeStatus(status, TournamentWorkflow.TournamentStatuses);
            if (normalizedStatus is null || !TournamentWorkflow.PublicStatuses.Contains(normalizedStatus))
                return BadRequest(new { message = "Trạng thái giải đấu không hợp lệ." });
            query = query.Where(item => item.Status == normalizedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var tournaments = await query
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(
            tournaments.Select(TournamentWorkflow.MapSummary),
            totalCount,
            page,
            pageSize));
    }

    [AllowAnonymous]
    [HttpGet("{identifier}")]
    public async Task<ActionResult<TournamentDetailResponse>> GetTournament(
        string identifier,
        CancellationToken cancellationToken)
    {
        var query = TournamentDetailQuery().Where(item =>
            TournamentWorkflow.PublicStatuses.Contains(item.Status));
        var tournament = int.TryParse(identifier, out var tournamentId)
            ? await query.SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken)
            : await query.SingleOrDefaultAsync(item => item.Slug == identifier, cancellationToken);

        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });

        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        return Ok(MapDetail(tournament, currentPlayerId));
    }

    [Authorize(Roles = "Player")]
    [HttpGet("me/registrations")]
    public async Task<ActionResult<IReadOnlyList<TournamentRegistrationResponse>>> GetMyRegistrations(
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return Forbid();

        var registrations = await RegistrationQuery()
            .AsNoTracking()
            .Where(item => item.CaptainPlayerId == playerId.Value)
            .OrderByDescending(item => item.RegisteredAt)
            .ToListAsync(cancellationToken);

        return Ok(registrations.Select(TournamentWorkflow.MapRegistration));
    }

    [Authorize(Roles = "Player")]
    [HttpPost("{tournamentId:int}/registrations")]
    public async Task<ActionResult<TournamentRegistrationResponse>> Register(
        int tournamentId,
        CreateTournamentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return Forbid();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var tournament = await _dbContext.Tournaments
            .Include(item => item.Divisions)
            .Include(item => item.Registrations)
            .SingleOrDefaultAsync(item => item.TournamentId == tournamentId, cancellationToken);
        if (tournament is null)
            return NotFound(new { message = "Không tìm thấy giải đấu." });
        if (!TournamentWorkflow.CanRegister(tournament, DateTime.UtcNow))
            return Conflict(new { message = "Giải đấu hiện không mở đăng ký." });
        if (tournament.Registrations.Any(item => item.CaptainPlayerId == playerId.Value))
            return Conflict(new { message = "Bạn đã đăng ký một đội tại giải đấu này." });

        var division = tournament.Divisions.SingleOrDefault(item =>
            item.TournamentDivisionId == request.TournamentDivisionId);
        if (division is null)
            return BadRequest(new { message = "Hạng mục không thuộc giải đấu." });
        if (division.Status != "Open")
            return Conflict(new { message = "Hạng mục đã khóa đăng ký." });

        var activeStatuses = new[] { "Pending", "Approved", "Waitlisted" };
        var divisionRegistrationCount = tournament.Registrations.Count(item =>
            item.TournamentDivisionId == division.TournamentDivisionId
            && activeStatuses.Contains(item.Status));
        var totalRegistrationCount = tournament.Registrations.Count(item =>
            activeStatuses.Contains(item.Status));
        var isFull = divisionRegistrationCount >= division.Capacity
            || totalRegistrationCount >= tournament.Capacity;

        var registration = new TournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            TournamentDivisionId = division.TournamentDivisionId,
            CaptainPlayerId = playerId.Value,
            TeamName = request.TeamName.Trim(),
            PartnerName = NormalizeOptional(request.PartnerName),
            RepresentativePhone = request.RepresentativePhone.Trim(),
            Status = isFull ? "Waitlisted" : "Pending",
            PaymentStatus = "Unpaid",
            AmountDue = division.EntryFee ?? tournament.EntryFee,
            RegisteredAt = DateTime.UtcNow
        };
        _dbContext.TournamentRegistrations.Add(registration);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (await _dbContext.TournamentRegistrations.AsNoTracking()
                .AnyAsync(item =>
                    item.TournamentId == tournamentId
                    && item.CaptainPlayerId == playerId.Value,
                    cancellationToken))
            {
                return Conflict(new { message = "Bạn đã đăng ký một đội tại giải đấu này." });
            }
            throw;
        }

        var created = await RegistrationQuery()
            .AsNoTracking()
            .SingleAsync(
                item => item.TournamentRegistrationId == registration.TournamentRegistrationId,
                cancellationToken);
        return CreatedAtAction(
            nameof(GetMyRegistrations),
            TournamentWorkflow.MapRegistration(created));
    }

    [Authorize(Roles = "Player")]
    [HttpPost("registrations/{registrationId:int}/payment")]
    public async Task<ActionResult<TournamentRegistrationResponse>> SubmitPayment(
        int registrationId,
        SubmitTournamentPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return Forbid();

        var registration = await RegistrationQuery()
            .SingleOrDefaultAsync(item =>
                item.TournamentRegistrationId == registrationId
                && item.CaptainPlayerId == playerId.Value,
                cancellationToken);
        if (registration is null)
            return NotFound(new { message = "Không tìm thấy đăng ký thuộc tài khoản của bạn." });
        if (registration.Status != "Approved")
            return Conflict(new { message = "Đội phải được duyệt trước khi thanh toán lệ phí." });
        if (registration.AmountDue == 0)
            return Conflict(new { message = "Đăng ký này không có lệ phí cần thanh toán." });
        if (registration.PaymentStatus == "Confirmed")
            return Conflict(new { message = "Lệ phí đã được xác nhận." });
        if (registration.Payment is not null && registration.Payment.Status != "Rejected")
            return Conflict(new { message = "Lệ phí đang chờ đối soát." });

        if (registration.Payment is null)
        {
            registration.Payment = new TournamentPayment
            {
                Amount = registration.AmountDue,
                PaymentMethod = request.PaymentMethod.Trim(),
                TransferContent = NormalizeOptional(request.TransferContent),
                ReceiptImageUrl = NormalizeOptional(request.ReceiptImageUrl),
                Status = "Pending",
                SubmittedAt = DateTime.UtcNow
            };
        }
        else
        {
            registration.Payment.Amount = registration.AmountDue;
            registration.Payment.PaymentMethod = request.PaymentMethod.Trim();
            registration.Payment.TransferContent = NormalizeOptional(request.TransferContent);
            registration.Payment.ReceiptImageUrl = NormalizeOptional(request.ReceiptImageUrl);
            registration.Payment.Status = "Pending";
            registration.Payment.SubmittedAt = DateTime.UtcNow;
            registration.Payment.VerifiedAt = null;
            registration.Payment.VerifiedByUserId = null;
            registration.Payment.RejectionReason = null;
        }

        registration.PaymentStatus = "Pending";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(TournamentWorkflow.MapRegistration(registration));
    }

    [Authorize(Roles = "Player")]
    [HttpPost("registrations/{registrationId:int}/payment-receipt")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<TournamentRegistrationResponse>> SubmitPaymentReceipt(
        int registrationId,
        [FromForm] SubmitTournamentPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return Forbid();
        if (request.Receipt is null || request.Receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (request.Receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(request.Receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });

        var registration = await RegistrationQuery()
            .SingleOrDefaultAsync(item =>
                item.TournamentRegistrationId == registrationId
                && item.CaptainPlayerId == playerId.Value,
                cancellationToken);
        if (registration is null)
            return NotFound(new { message = "Không tìm thấy đăng ký thuộc tài khoản của bạn." });
        if (registration.Status != "Approved")
            return Conflict(new { message = "Đội phải được duyệt trước khi thanh toán lệ phí." });
        if (registration.AmountDue == 0)
            return Conflict(new { message = "Đăng ký này không có lệ phí cần thanh toán." });
        if (registration.PaymentStatus == "Confirmed")
            return Conflict(new { message = "Lệ phí đã được xác nhận." });
        if (registration.Payment is not null && registration.Payment.Status != "Rejected")
            return Conflict(new { message = "Lệ phí đang chờ đối soát." });

        var receiptUrl = await SaveReceiptAsync(
            registration.TournamentRegistrationId,
            request.Receipt,
            cancellationToken);
        var payment = registration.Payment ?? new TournamentPayment
        {
            TournamentRegistrationId = registration.TournamentRegistrationId
        };
        payment.Amount = registration.AmountDue;
        payment.PaymentMethod = "BankTransfer";
        payment.TransferContent = NormalizeOptional(request.TransferContent);
        payment.ReceiptImageUrl = receiptUrl;
        payment.Status = "Pending";
        payment.SubmittedAt = DateTime.UtcNow;
        payment.VerifiedAt = null;
        payment.VerifiedByUserId = null;
        payment.RejectionReason = null;
        if (registration.Payment is null)
            registration.Payment = payment;
        registration.PaymentStatus = "Pending";

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(TournamentWorkflow.MapRegistration(registration));
    }

    [Authorize(Roles = "Player")]
    [HttpDelete("registrations/{registrationId:int}")]
    public async Task<IActionResult> CancelRegistration(
        int registrationId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return Forbid();

        var registration = await _dbContext.TournamentRegistrations
            .Include(item => item.Tournament)
            .SingleOrDefaultAsync(item =>
                item.TournamentRegistrationId == registrationId
                && item.CaptainPlayerId == playerId.Value,
                cancellationToken);
        if (registration is null)
            return NotFound(new { message = "Không tìm thấy đăng ký thuộc tài khoản của bạn." });
        if (DateOnly.FromDateTime(DateTime.UtcNow) >= registration.Tournament.StartDate)
            return Conflict(new { message = "Không thể hủy đăng ký từ ngày giải bắt đầu." });
        if (registration.PaymentStatus == "Confirmed")
            return Conflict(new { message = "Vui lòng liên hệ ban tổ chức để xử lý hoàn lệ phí." });

        registration.Status = "Cancelled";
        registration.CheckInCode = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
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

    internal static TournamentDetailResponse MapDetail(
        Tournament tournament,
        int? currentPlayerId,
        bool forceIncludeResults = false)
    {
        var summary = TournamentWorkflow.MapSummary(tournament);
        var includeResults = forceIncludeResults || tournament.ResultsPublishedAt is not null;
        var myRegistration = currentPlayerId is null
            ? null
            : tournament.Registrations.SingleOrDefault(item =>
                item.CaptainPlayerId == currentPlayerId.Value);
        TournamentRegistrationResponse? mappedMyRegistration = null;
        if (myRegistration is not null)
        {
            myRegistration.Tournament = tournament;
            if (myRegistration.Division is null)
            {
                myRegistration.Division = tournament.Divisions.Single(item =>
                    item.TournamentDivisionId == myRegistration.TournamentDivisionId);
            }

            mappedMyRegistration = TournamentWorkflow.MapRegistration(myRegistration);
        }

        return new TournamentDetailResponse
        {
            TournamentId = summary.TournamentId,
            Slug = summary.Slug,
            Name = summary.Name,
            Status = summary.Status,
            ImageUrl = summary.ImageUrl,
            City = summary.City,
            VenueName = summary.VenueName,
            StartDate = summary.StartDate,
            EndDate = summary.EndDate,
            RegistrationDeadline = summary.RegistrationDeadline,
            Format = summary.Format,
            SkillLevel = summary.SkillLevel,
            Capacity = summary.Capacity,
            RegisteredCount = summary.RegisteredCount,
            EntryFee = summary.EntryFee,
            PrizePool = summary.PrizePool,
            Description = summary.Description,
            Address = tournament.Address,
            OrganizerName = tournament.OrganizerName,
            OrganizerPhone = tournament.OrganizerPhone,
            BracketType = tournament.BracketType,
            Rules = (tournament.Rules ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Divisions = tournament.Divisions
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Name)
                .Select(item => new TournamentDivisionResponse
                {
                    TournamentDivisionId = item.TournamentDivisionId,
                    Name = item.Name,
                    Description = item.Description,
                    SkillLevel = item.SkillLevel,
                    Capacity = item.Capacity,
                    RegisteredCount = tournament.Registrations.Count(registration =>
                        registration.TournamentDivisionId == item.TournamentDivisionId
                        && registration.Status is "Pending" or "Approved" or "Waitlisted"),
                    EntryFee = item.EntryFee ?? tournament.EntryFee,
                    Status = TournamentWorkflow.ToApiValue(item.Status),
                    DisplayOrder = item.DisplayOrder
                })
                .ToList(),
            Teams = tournament.Registrations
                .Where(item => item.Status is "Approved" or "Waitlisted")
                .OrderBy(item => item.RegisteredAt)
                .Select(item => new TournamentTeamResponse
                {
                    RegistrationId = item.TournamentRegistrationId,
                    TeamName = item.TeamName,
                    DivisionName = item.Division.Name,
                    Area = item.CaptainPlayer.User.City,
                    SkillLevel = item.Division.SkillLevel,
                    Status = TournamentWorkflow.ToApiValue(item.Status)
                })
                .ToList(),
            Matches = tournament.Matches
                .OrderBy(item => item.ScheduledAt)
                .ThenBy(item => item.MatchNumber)
                .Select(item => TournamentWorkflow.MapMatch(item, includeResults))
                .ToList(),
            MyRegistration = mappedMyRegistration,
            ResultsPublishedAt = tournament.ResultsPublishedAt is null
                ? null
                : TournamentWorkflow.AsUtc(tournament.ResultsPublishedAt.Value)
        };
    }

    private async Task<int?> CurrentPlayerIdAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId is null
            ? null
            : await _dbContext.Players.AsNoTracking()
                .Where(item => item.UserId == userId.Value)
                .Select(item => (int?)item.PlayerId)
                .SingleOrDefaultAsync(cancellationToken);
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<string> SaveReceiptAsync(
        int registrationId,
        IFormFile receipt,
        CancellationToken cancellationToken)
    {
        var extension = receipt.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"tournament-{registrationId}-{Guid.NewGuid():N}{extension}";
        var root = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "payment-receipts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await using var stream = System.IO.File.Create(path);
        await receipt.CopyToAsync(stream, cancellationToken);
        return $"/uploads/payment-receipts/{fileName}";
    }

    private static string? NormalizeStatus(string value, IEnumerable<string> allowed) =>
        allowed.FirstOrDefault(item => item.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
