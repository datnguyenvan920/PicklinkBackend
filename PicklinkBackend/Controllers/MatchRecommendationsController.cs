using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

public partial class MatchController
{
    [Authorize]
    [HttpGet("player-recommendations")]
    public async Task<ActionResult<List<MatchPlayerRecommendationResponse>>> GetPlayerRecommendations(
        double radiusKm = 5,
        double? latitude = null,
        double? longitude = null,
        string? province = null,
        string? ward = null,
        int minSkillLevel = 1,
        int maxSkillLevel = 5,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        if (radiusKm is < 0.5 or > 10)
            return BadRequest(new { message = "Bán kính tìm người chơi phải từ 0,5 đến 10 km." });
        if (latitude.HasValue != longitude.HasValue)
            return BadRequest(new { message = "Cần cung cấp đồng thời vĩ độ và kinh độ." });
        if (minSkillLevel is < 1 or > 5 || maxSkillLevel is < 1 or > 5 || minSkillLevel > maxSkillLevel)
            return BadRequest(new { message = "Khoảng trình độ phải nằm trong mức 1 đến 5." });

        var criteria = new PlayerRecommendationCriteria(
            playerId.Value,
            latitude,
            longitude,
            radiusKm,
            province,
            ward,
            minSkillLevel,
            maxSkillLevel);
        return Ok(await LoadPlayerRecommendationsAsync(
            criteria,
            Math.Clamp(limit, 1, 50),
            new HashSet<int>(),
            cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitations")]
    public async Task<ActionResult<OpenMatchDetailResponse>> InviteMatchPlayers(
        int matchId,
        InviteMatchPlayersRequest request,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status != "Recruiting")
            return Conflict(new { message = "Chỉ có thể mời người chơi khi phòng đang tuyển người." });

        var excludedPlayerIds = match.MatchParticipants.Select(item => item.PlayerId).ToHashSet();
        var criteria = new PlayerRecommendationCriteria(
            hostPlayerId.Value,
            match.SearchLatitude,
            match.SearchLongitude,
            match.SearchRadiusKm,
            match.Province,
            match.Ward,
            match.MinSkillLevel,
            match.MaxSkillLevel);
        var recommendations = await LoadPlayerRecommendationsAsync(criteria, 50, excludedPlayerIds, cancellationToken);
        IReadOnlyCollection<MatchPlayerRecommendationResponse> selected;
        if (request.Automatic)
        {
            var availableSlots = Math.Max(match.RequiredPlayerCount - ApprovedParticipants(match).Count, 1);
            selected = recommendations.Take(Math.Clamp(availableSlots * 3, 3, 12)).ToList();
        }
        else
        {
            var requestedIds = request.PlayerIds.Where(id => id > 0).Distinct().Take(20).ToHashSet();
            selected = recommendations.Where(item => requestedIds.Contains(item.PlayerId)).ToList();
        }

        var now = DateTime.UtcNow;
        var selectedIds = selected.Select(item => item.PlayerId).ToList();
        var players = await _db.Players
            .Include(item => item.User)
            .Where(item => selectedIds.Contains(item.PlayerId))
            .ToListAsync(cancellationToken);
        foreach (var invitedPlayer in players)
        {
            match.MatchParticipants.Add(new MatchParticipant
            {
                PlayerId = invitedPlayer.PlayerId,
                Status = "Invited",
                IsHost = false,
                RequestedAt = now
            });
            _notifications.Add(new NotificationInput(
                UserId: invitedPlayer.UserId,
                Type: NotificationTypes.Match,
                Title: "Lời mời ghép trận",
                Message: $"{match.HostPlayer?.User.Username ?? "Một người chơi"} mời bạn tham gia trận \"{match.Title}\".",
                Tone: NotificationTones.Urgent,
                LinkTo: $"/matches/{match.MatchId}",
                LinkLabel: "Xem trận"));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        if (players.Count > 0) _matchRealtime.Publish(matchId, "PlayersInvited");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitation/accept")]
    public async Task<ActionResult<OpenMatchDetailResponse>> AcceptMatchInvitation(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (match.Status != "Recruiting")
            return Conflict(new { message = "Phòng hiện không còn nhận người chơi." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant?.Status != "Invited")
            return Conflict(new { message = "Bạn không có lời mời đang chờ cho trận này." });
        if (ApprovedParticipants(match).Count >= match.RequiredPlayerCount)
            return Conflict(new { message = "Phòng đã đủ người." });
        if (player.SkillLevel < match.MinSkillLevel || player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new { message = "Trình độ hiện tại của bạn không còn phù hợp với trận." });

        participant.Status = "Approved";
        participant.RespondedAt = DateTime.UtcNow;
        await AddConversationParticipantAsync(match, player.UserId, cancellationToken);
        if (match.HostPlayer is not null)
        {
            _notifications.Add(new NotificationInput(
                UserId: match.HostPlayer.UserId,
                Type: NotificationTypes.Match,
                Title: "Lời mời đã được chấp nhận",
                Message: $"{player.User.Username} đã chấp nhận lời mời tham gia trận \"{match.Title}\".",
                Tone: NotificationTones.Success,
                LinkTo: $"/matches/{match.MatchId}",
                LinkLabel: "Xem trận"));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _matchRealtime.Publish(matchId, "InvitationAccepted");
        return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitation/decline")]
    public async Task<ActionResult<OpenMatchDetailResponse>> DeclineMatchInvitation(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant?.Status != "Invited")
            return Conflict(new { message = "Bạn không có lời mời đang chờ cho trận này." });

        participant.Status = "Rejected";
        participant.RespondedAt = DateTime.UtcNow;
        if (match.HostPlayer is not null)
        {
            _notifications.Add(new NotificationInput(
                UserId: match.HostPlayer.UserId,
                Type: NotificationTypes.Match,
                Title: "Lời mời bị từ chối",
                Message: $"{player.User.Username} đã từ chối lời mời tham gia trận \"{match.Title}\".",
                Tone: NotificationTones.Default,
                LinkTo: $"/matches/{match.MatchId}",
                LinkLabel: "Xem trận"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();
        _matchRealtime.Publish(matchId, "InvitationDeclined");
        return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
    }

    private async Task<List<MatchPlayerRecommendationResponse>> LoadPlayerRecommendationsAsync(
        PlayerRecommendationCriteria criteria,
        int limit,
        IReadOnlySet<int> excludedPlayerIds,
        CancellationToken cancellationToken)
    {
        var candidates = await _db.Players.AsNoTracking()
            .Where(player =>
                player.PlayerId != criteria.CurrentPlayerId
                && player.SkillLevel >= criteria.MinSkillLevel
                && player.SkillLevel <= criteria.MaxSkillLevel
                && !excludedPlayerIds.Contains(player.PlayerId))
            .Select(player => new PlayerRecommendationCandidate
            {
                PlayerId = player.PlayerId,
                PlayerName = player.User.Username,
                AvatarUrl = player.User.ProfileImageUrl,
                SkillLevel = player.SkillLevel,
                Prestige = player.Prestige,
                City = player.User.City,
                Commune = player.User.Commune,
                PreferredTimeSlot = player.PreferredTimeSlot,
                Latitude = player.HostedMatches
                    .Where(match => match.SearchLatitude.HasValue && match.SearchLongitude.HasValue)
                    .OrderByDescending(match => match.CreatedAt)
                    .Select(match => match.SearchLatitude)
                    .FirstOrDefault(),
                Longitude = player.HostedMatches
                    .Where(match => match.SearchLatitude.HasValue && match.SearchLongitude.HasValue)
                    .OrderByDescending(match => match.CreatedAt)
                    .Select(match => match.SearchLongitude)
                    .FirstOrDefault()
            })
            .Take(1000)
            .ToListAsync(cancellationToken);

        var normalizedProvince = NormalizeAreaForMatching(criteria.Province);
        var normalizedWard = NormalizeAreaForMatching(criteria.Ward);
        var targetSkill = (criteria.MinSkillLevel + criteria.MaxSkillLevel) / 2d;
        return candidates
            .Select(candidate =>
            {
                double? distance = null;
                if (criteria.Latitude.HasValue
                    && criteria.Longitude.HasValue
                    && candidate.Latitude.HasValue
                    && candidate.Longitude.HasValue)
                {
                    distance = DistanceKm(
                        criteria.Latitude.Value,
                        criteria.Longitude.Value,
                        candidate.Latitude.Value,
                        candidate.Longitude.Value);
                }

                var sameProvince = AreaMatches(candidate.City, normalizedProvince);
                var sameWard = sameProvince && AreaMatches(candidate.Commune, normalizedWard);
                var eligible = distance.HasValue
                    ? distance.Value <= criteria.RadiusKm
                    : sameWard;
                if (!eligible) return null;

                return new RankedPlayerRecommendation(
                    new MatchPlayerRecommendationResponse
                    {
                        PlayerId = candidate.PlayerId,
                        PlayerName = candidate.PlayerName,
                        AvatarUrl = candidate.AvatarUrl,
                        SkillLevel = candidate.SkillLevel,
                        Prestige = candidate.Prestige,
                        City = candidate.City,
                        Commune = candidate.Commune,
                        PreferredTimeSlot = candidate.PreferredTimeSlot,
                        DistanceKm = distance.HasValue ? Math.Round(distance.Value, 2) : null,
                        MatchReason = distance.HasValue
                            ? $"Cách khoảng {distance.Value:0.##} km"
                            : "Cùng xã/phường trong hồ sơ"
                    },
                    distance ?? 0,
                    Math.Abs(candidate.SkillLevel - targetSkill));
            })
            .Where(item => item is not null)
            .OrderBy(item => item!.Distance)
            .ThenBy(item => item!.SkillDifference)
            .ThenByDescending(item => item!.Response.Prestige)
            .Take(limit)
            .Select(item => item!.Response)
            .ToList();
    }

    private static bool AreaMatches(string? value, string normalizedTarget)
    {
        if (string.IsNullOrEmpty(normalizedTarget)) return false;
        var normalizedValue = NormalizeAreaForMatching(value);
        if (string.IsNullOrEmpty(normalizedValue)) return false;
        return normalizedValue == normalizedTarget
            || normalizedValue.Contains(normalizedTarget, StringComparison.Ordinal)
            || normalizedTarget.Contains(normalizedValue, StringComparison.Ordinal);
    }

    private static string NormalizeAreaForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private sealed record PlayerRecommendationCriteria(
        int CurrentPlayerId,
        double? Latitude,
        double? Longitude,
        double RadiusKm,
        string? Province,
        string? Ward,
        int MinSkillLevel,
        int MaxSkillLevel);

    private sealed record RankedPlayerRecommendation(
        MatchPlayerRecommendationResponse Response,
        double Distance,
        double SkillDifference);

    private sealed class PlayerRecommendationCandidate
    {
        public int PlayerId { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public string? AvatarUrl { get; init; }
        public double SkillLevel { get; init; }
        public int Prestige { get; init; }
        public string? City { get; init; }
        public string? Commune { get; init; }
        public string? PreferredTimeSlot { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
    }
}
