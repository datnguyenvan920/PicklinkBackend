using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories.Implementations;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<User> Users => _dbContext.Users;
    public IQueryable<Player> Players => _dbContext.Players;
    public IQueryable<MatchParticipant> MatchParticipants => _dbContext.MatchParticipants;

    public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.FindAsync([userId], cancellationToken).AsTask();
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        EmailExistsAsync(email, cancellationToken);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AnyAsync(u => u.Username == username, cancellationToken);
    }

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        UsernameExistsAsync(username, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken = default)
    {
        await _dbContext.Users.AddAsync(user, cancellationToken);
    }

    public async Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _dbContext.Players.AddAsync(player, cancellationToken);
    }

    public async Task AddPasswordResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
    {
        await _dbContext.PasswordResetTokens.AddAsync(token, cancellationToken);
    }

    public Task<List<PasswordResetToken>> GetActivePasswordResetTokensAsync(
        int userId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);
    }

    public Task<PasswordResetToken?> GetValidPasswordResetTokenAsync(
        int userId,
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.TokenHash == tokenHash && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsPasswordResetTokenValidAsync(
        string email,
        string tokenHash,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .AsNoTracking()
            .AnyAsync(t => t.User.Email == email && t.TokenHash == tokenHash && t.UsedAt == null && t.ExpiresAt > now, cancellationToken);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
