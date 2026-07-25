using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface IUserRepository
{
    IQueryable<User> Users { get; }
    IQueryable<Player> Players { get; }
    IQueryable<MatchParticipant> MatchParticipants { get; }

    Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task AddUserAsync(User user, CancellationToken cancellationToken = default);
    Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default);
    Task AddPasswordResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken = default);
    Task<List<PasswordResetToken>> GetActivePasswordResetTokensAsync(int userId, DateTime now, CancellationToken cancellationToken = default);
    Task<PasswordResetToken?> GetValidPasswordResetTokenAsync(int userId, string tokenHash, CancellationToken cancellationToken = default);
    Task<bool> IsPasswordResetTokenValidAsync(string email, string tokenHash, DateTime now, CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
