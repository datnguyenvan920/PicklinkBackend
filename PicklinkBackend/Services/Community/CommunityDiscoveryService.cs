using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Community;

public class CommunityDiscoveryService
{
    private readonly ApplicationDbContext _dbContext;

    public CommunityDiscoveryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OutstandingPlayerResponse>> GetOutstandingPlayersAsync(
        CancellationToken cancellationToken)
    {
        return await _dbContext.Players
            .AsNoTracking()
            .Include(player => player.User)
            .OrderByDescending(player => player.Prestige)
            .ThenByDescending(player => player.SkillLevel)
            .Take(5)
            .Select(player => new OutstandingPlayerResponse(
                player.User.UserId,
                player.User.Username,
                player.SkillLevel.ToString("0.0"),
                player.User.ProfileImageUrl))
            .ToListAsync(cancellationToken);
    }
}
