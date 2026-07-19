using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Players;

public sealed class PlayerProfileService
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

    public PlayerProfileService(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    public async Task<PlayerProfileResult<UserProfileResponse>> GetMeAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerProfileResult<UserProfileResponse>.Unauthorized();

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        return response is null
            ? PlayerProfileResult<UserProfileResponse>.NotFound()
            : PlayerProfileResult<UserProfileResponse>.Success(response);
    }

    public async Task<PlayerProfileResult<PublicPlayerProfileResponse>> GetPublicPlayerProfileAsync(
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

        return profile is null
            ? PlayerProfileResult<PublicPlayerProfileResponse>.NotFound()
            : PlayerProfileResult<PublicPlayerProfileResponse>.Success(profile);
    }

    public async Task<PlayerProfileResult<UserProfileResponse>> UploadAvatarAsync(
        IFormFile avatar,
        int? userId,
        string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerProfileResult<UserProfileResponse>.Unauthorized();

        if (avatar.Length == 0)
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Vui long chon anh dai dien.");

        if (avatar.Length > MaxAvatarBytes)
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Anh dai dien khong duoc vuot qua 2MB.");

        var extension = Path.GetExtension(avatar.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedAvatarExtensions.Contains(extension))
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Chi ho tro anh JPG, PNG, WEBP hoac GIF.");

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(user => user.UserId == userId.Value, cancellationToken);
        if (user is null) return PlayerProfileResult<UserProfileResponse>.NotFound();

        var webRootPath = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var avatarDirectory = Path.Combine(webRootPath, "uploads", "avatars");
        Directory.CreateDirectory(avatarDirectory);

        var fileName = $"user-{user.UserId}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(avatarDirectory, fileName);

        await using (var stream = File.Create(filePath))
        {
            await avatar.CopyToAsync(stream, cancellationToken);
        }

        user.ProfileImageUrl = $"{publicBaseUrl}/uploads/avatars/{fileName}";
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        return response is null
            ? PlayerProfileResult<UserProfileResponse>.NotFound()
            : PlayerProfileResult<UserProfileResponse>.Success(response);
    }

    public async Task<PlayerProfileResult<UserProfileResponse>> UpdateMeAsync(
        UpdateUserProfileRequest request,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerProfileResult<UserProfileResponse>.Unauthorized();

        var username = request.Username.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Vui long nhap ten nguoi dung.");

        if (request.BirthDate > DateOnly.FromDateTime(VietnamTime.Now))
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Ngay sinh khong duoc lon hon ngay hien tai.");

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(user => user.UserId == userId.Value, cancellationToken);
        if (user is null) return PlayerProfileResult<UserProfileResponse>.NotFound();

        var usernameIsUsed = await _dbContext.Users.AnyAsync(
            existingUser => existingUser.UserId != userId.Value &&
                            existingUser.Username == username,
            cancellationToken);
        if (usernameIsUsed)
            return PlayerProfileResult<UserProfileResponse>.Conflict("Ten nguoi dung nay da duoc su dung.");

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
        return response is null
            ? PlayerProfileResult<UserProfileResponse>.NotFound()
            : PlayerProfileResult<UserProfileResponse>.Success(response);
    }

    private async Task<UserProfileResponse?> BuildProfileResponseAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);
        if (user is null) return null;

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

        if (player is null) return response;

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PlayerProfileResult<T>(
    PlayerProfileResultStatus Status,
    T? Value,
    string? ErrorMessage)
{
    public static PlayerProfileResult<T> Success(T value) =>
        new(PlayerProfileResultStatus.Success, value, ErrorMessage: null);

    public static PlayerProfileResult<T> BadRequest(string errorMessage) =>
        new(PlayerProfileResultStatus.BadRequest, Value: default, errorMessage);

    public static PlayerProfileResult<T> Unauthorized() =>
        new(PlayerProfileResultStatus.Unauthorized, Value: default, ErrorMessage: null);

    public static PlayerProfileResult<T> NotFound() =>
        new(PlayerProfileResultStatus.NotFound, Value: default, ErrorMessage: null);

    public static PlayerProfileResult<T> Conflict(string errorMessage) =>
        new(PlayerProfileResultStatus.Conflict, Value: default, errorMessage);
}

public enum PlayerProfileResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    NotFound,
    Conflict
}