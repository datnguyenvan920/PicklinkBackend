using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Players;
using PicklinkBackend.Services.Shared;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Services.Players.Implementations;

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

    private readonly IUserRepository _userRepository;
    private readonly IWebHostEnvironment _environment;
    private readonly CloudinaryDestroyService _cloudinaryDestroy;

    public PlayerProfileService(
        IUserRepository userRepository,
        IWebHostEnvironment environment,
        CloudinaryDestroyService cloudinaryDestroy)
    {
        _userRepository = userRepository;
        _environment = environment;
        _cloudinaryDestroy = cloudinaryDestroy;
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
        var profile = await _userRepository.Players
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

        if (!await ImageUploadPolicy.HasValidSignatureAsync(avatar, cancellationToken))
            return PlayerProfileResult<UserProfileResponse>.BadRequest("Noi dung tep khong khop voi dinh dang anh.");

        extension = avatar.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null) return PlayerProfileResult<UserProfileResponse>.NotFound();

        var oldAvatarUrl = user.ProfileImageUrl;

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
        await _userRepository.SaveChangesAsync(cancellationToken);
        await CleanupPreviousAvatarAsync(oldAvatarUrl, user.ProfileImageUrl);

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

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null) return PlayerProfileResult<UserProfileResponse>.NotFound();

        var usernameIsUsed = await _userRepository.Users.AnyAsync(
            existingUser => existingUser.UserId != userId.Value &&
                            existingUser.Username == username,
            cancellationToken);
        if (usernameIsUsed)
            return PlayerProfileResult<UserProfileResponse>.Conflict("Ten nguoi dung nay da duoc su dung.");

        var oldAvatarUrl = user.ProfileImageUrl;
        var newAvatarUrl = NormalizeOptional(request.ProfileImageUrl);

        user.Username = username;
        user.City = NormalizeOptional(request.City);
        user.Commune = NormalizeOptional(request.Commune);
        user.ProfileImageUrl = newAvatarUrl;

        var player = await _userRepository.Players
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
            await _userRepository.AddPlayerAsync(player, cancellationToken);
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

        await _userRepository.SaveChangesAsync(cancellationToken);
        await CleanupPreviousAvatarAsync(oldAvatarUrl, newAvatarUrl);

        var response = await BuildProfileResponseAsync(userId.Value, cancellationToken);
        return response is null
            ? PlayerProfileResult<UserProfileResponse>.NotFound()
            : PlayerProfileResult<UserProfileResponse>.Success(response);
    }

    private async Task<UserProfileResponse?> BuildProfileResponseAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);
        if (user is null) return null;

        var player = await _userRepository.Players
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
        response.MatchesPlayed = await _userRepository.MatchParticipants
            .AsNoTracking()
            .CountAsync(participant => participant.PlayerId == player.PlayerId, cancellationToken);

        response.MatchHistory = await _userRepository.MatchParticipants
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

    private async Task CleanupPreviousAvatarAsync(string? oldAvatarUrl, string? newAvatarUrl)
    {
        if (string.IsNullOrWhiteSpace(oldAvatarUrl) || string.Equals(oldAvatarUrl, newAvatarUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (CloudinaryDestroyService.TryExtractPublicId(oldAvatarUrl, out var publicId))
        {
            await _cloudinaryDestroy.DestroyAsync(publicId);
            return;
        }

        try
        {
            var marker = "/uploads/avatars/";
            var markerIndex = oldAvatarUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) return;

            var relativePath = Uri.UnescapeDataString(oldAvatarUrl[(markerIndex + 1)..]).Replace('/', Path.DirectorySeparatorChar);
            var qIndex = relativePath.IndexOf('?');
            if (qIndex != -1) relativePath = relativePath[..qIndex];

            var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var avatarRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "avatars")) + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(avatarRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Suppress background file deletion exception
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
