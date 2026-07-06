using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public ProfileController(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpGet("players/{playerId:int}")]
    public async Task<ActionResult<PublicPlayerProfileResponse>> GetPublicPlayerProfile(
        int playerId,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.Players
            .AsNoTracking()
            .Where(player => player.PlayerId == playerId)
            .Select(player => new PublicPlayerProfileResponse
            {
                PlayerId = player.PlayerId,
                Username = player.User.Username,
                ProfileImageUrl = player.User.ProfileImageUrl,
                City = player.User.City,
                Commune = player.User.Commune,
                SkillLevel = player.SkillLevel,
                Prestige = player.Prestige,
                PlayerSubType = player.PlayerSubType,
                PlayFrequency = player.PlayFrequency,
                PreferredTimeSlot = player.PreferredTimeSlot,
                Bio = player.Bio,
                MatchesPlayed = player.MatchParticipants.Count(participant =>
                    participant.Status == "Approved" || participant.Status == "Accepted")
            })
            .SingleOrDefaultAsync(cancellationToken);

        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost("me/avatar")]
    [RequestSizeLimit(MaxAvatarBytes + 1024 * 100)]
    public async Task<ActionResult<UserProfileResponse>> UploadAvatar(
        IFormFile avatar,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (avatar.Length == 0)
        {
            return BadRequest(new { message = "Vui lòng chọn ảnh đại diện." });
        }

        if (avatar.Length > MaxAvatarBytes)
        {
            return BadRequest(new { message = "Ảnh đại diện không được vượt quá 2MB." });
        }

        var extension = Path.GetExtension(avatar.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedAvatarExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Chỉ hỗ trợ ảnh JPG, PNG, WEBP hoặc GIF." });
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(user => user.UserId == userId.Value, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var webRootPath = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var avatarDirectory = Path.Combine(webRootPath, "uploads", "avatars");
        Directory.CreateDirectory(avatarDirectory);

        var fileName = $"user-{user.UserId}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(avatarDirectory, fileName);

        await using (var stream = System.IO.File.Create(filePath))
        {
            await avatar.CopyToAsync(stream, cancellationToken);
        }

        user.ProfileImageUrl = $"{Request.Scheme}://{Request.Host}/uploads/avatars/{fileName}";
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileResponse>> UpdateMe(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var username = request.Username.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return BadRequest(new { message = "Vui lòng nhập tên người dùng." });
        }

        if (request.BirthDate > DateOnly.FromDateTime(DateTime.Today))
        {
            return BadRequest(new { message = "Ngày sinh không được lớn hơn ngày hiện tại." });
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(user => user.UserId == userId.Value, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var usernameIsUsed = await _dbContext.Users.AnyAsync(
            existingUser => existingUser.UserId != userId.Value &&
                            existingUser.Username == username,
            cancellationToken);

        if (usernameIsUsed)
        {
            return Conflict(new { message = "Tên người dùng này đã được sử dụng." });
        }

        user.Username = username;
        user.City = NormalizeOptional(request.City);
        user.Commune = NormalizeOptional(request.Commune);
        user.ProfileImageUrl = NormalizeOptional(request.ProfileImageUrl);

        var player = await _dbContext.Players
            .Where(player => player.UserId == userId.Value)
            .OrderByDescending(player => player.Prestige)
            .ThenByDescending(player => player.SkillLevel)
            .ThenByDescending(player => player.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (player is null)
        {
            player = new Player
            {
                UserId = user.UserId,
                Prestige = 0
            };
            _dbContext.Players.Add(player);
        }

        player.SkillLevel = request.SkillLevel;
        player.PlayerSubType = NormalizeOptional(request.PlayerSubType);
        player.PlayFrequency = NormalizeOptional(request.PlayFrequency);
        player.PreferredTimeSlot = NormalizeOptional(request.PreferredTimeSlot);
        player.Bio = NormalizeOptional(request.Bio);
        player.BirthDate = request.BirthDate;
        player.Gender = NormalizeOptional(request.Gender);
        player.HeightCm = request.HeightCm;
        player.WeightKg = request.WeightKg;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    private async Task<UserProfileResponse?> BuildProfileResponseAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var player = await _dbContext.Players
            .AsNoTracking()
            .Where(player => player.UserId == userId)
            .OrderByDescending(player => player.Prestige)
            .ThenByDescending(player => player.SkillLevel)
            .ThenByDescending(player => player.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new UserProfileResponse
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            UserType = user.UserType,
            ProfileImageUrl = user.ProfileImageUrl,
            City = user.City,
            Commune = user.Commune
        };

        if (player is null)
        {
            return response;
        }

        response.PlayerId = player.PlayerId;
        response.SkillLevel = player.SkillLevel;
        response.Prestige = player.Prestige;
        response.PlayerSubType = player.PlayerSubType;
        response.PlayFrequency = player.PlayFrequency;
        response.PreferredTimeSlot = player.PreferredTimeSlot;
        response.Bio = player.Bio;
        response.BirthDate = player.BirthDate;
        response.Gender = player.Gender;
        response.HeightCm = player.HeightCm;
        response.WeightKg = player.WeightKg;
        response.MatchesPlayed = await _dbContext.MatchParticipants
            .AsNoTracking()
            .CountAsync(participant => participant.PlayerId == player.PlayerId, cancellationToken);

        response.MatchHistory = await _dbContext.MatchParticipants
            .AsNoTracking()
            .Where(participant => participant.PlayerId == player.PlayerId)
            .OrderByDescending(participant => participant.Match.MatchTime)
            .Select(participant => new MatchHistoryItemResponse
            {
                MatchId = participant.MatchId,
                MatchType = participant.Match.MatchType,
                MatchSkillLevel = participant.Match.MatchSkillLevel,
                MatchTime = participant.Match.MatchTime,
                Status = participant.Match.Status,
                ParticipantClass = participant.Class,
                VenueName = participant.Match.Bookings
                    .OrderBy(booking => booking.StartTime)
                    .Select(booking => booking.Court.Venue.VenueName)
                    .FirstOrDefault(),
                CourtNumber = participant.Match.Bookings
                    .OrderBy(booking => booking.StartTime)
                    .Select(booking => (int?)booking.Court.CourtNumber)
                    .FirstOrDefault(),
                ScoreInfo = participant.Match.Scorecards
                    .OrderBy(scorecard => scorecard.GameId)
                    .Select(scorecard => scorecard.ScoreInfo)
                    .FirstOrDefault(),
                CheckInStatus = participant.Match.MatchCheckIns
                    .Where(checkIn => checkIn.PlayerId == player.PlayerId)
                    .Select(checkIn => checkIn.Status)
                    .FirstOrDefault()
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        return response;
    }

    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
