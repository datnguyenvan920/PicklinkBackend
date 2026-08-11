using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories.Implementations;

public class CommunityRepository : ICommunityRepository
{
    private readonly ApplicationDbContext _dbContext;

    public CommunityRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<SocialGroup> SocialGroups => _dbContext.SocialGroups;
    public IQueryable<GroupMember> GroupMembers => _dbContext.GroupMembers;
    public IQueryable<Post> GroupPosts => _dbContext.Posts;
    public IQueryable<PostComment> GroupComments => _dbContext.PostComments;
    public IQueryable<PostLike> PostLikes => _dbContext.PostLikes;
    public IQueryable<Conversation> Conversations => _dbContext.Conversations;
    public IQueryable<ConversationParticipant> ConversationParticipants => _dbContext.ConversationParticipants;
    public IQueryable<Message> Messages => _dbContext.Messages;
    public IQueryable<User> Users => _dbContext.Users;
    public IQueryable<Player> Players => _dbContext.Players;
    public IQueryable<MatchParticipant> MatchParticipants => _dbContext.MatchParticipants;
    public IQueryable<MatchSlotReplacementRequest> MatchSlotReplacementRequests => _dbContext.MatchSlotReplacementRequests;
    public IQueryable<CommunityReport> CommunityReports => _dbContext.CommunityReports;
    public IQueryable<Friendship> Friendships => _dbContext.Friendships;
    public IQueryable<GroupImage> GroupImages => _dbContext.GroupImages;

    public Task<SocialGroup?> GetGroupByIdAsync(int groupId, CancellationToken cancellationToken = default)
    {
        return _dbContext.SocialGroups
            .Include(g => g.GroupMembers)
            .Include(g => g.Posts)
            .SingleOrDefaultAsync(g => g.GroupId == groupId, cancellationToken);
    }

    public Task<Conversation?> GetConversationByIdAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Conversations
            .Include(c => c.ConversationParticipants)
            .Include(c => c.Messages)
            .SingleOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);
    }

    public async Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _dbContext.Players.AddAsync(player, cancellationToken);
    }

    public async Task AddGroupAsync(SocialGroup group, CancellationToken cancellationToken = default)
    {
        await _dbContext.SocialGroups.AddAsync(group, cancellationToken);
    }

    public async Task AddPostAsync(Post post, CancellationToken cancellationToken = default)
    {
        await _dbContext.Posts.AddAsync(post, cancellationToken);
    }

    public async Task AddCommentAsync(PostComment comment, CancellationToken cancellationToken = default)
    {
        await _dbContext.PostComments.AddAsync(comment, cancellationToken);
    }

    public async Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default)
    {
        await _dbContext.GroupMembers.AddAsync(member, cancellationToken);
    }

    public Task RemoveMemberAsync(GroupMember member, CancellationToken cancellationToken = default)
    {
        _dbContext.GroupMembers.Remove(member);
        return Task.CompletedTask;
    }

    public async Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await _dbContext.Conversations.AddAsync(conversation, cancellationToken);
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        await _dbContext.Messages.AddAsync(message, cancellationToken);
    }

    public async Task AddLikeAsync(PostLike like, CancellationToken cancellationToken = default)
    {
        await _dbContext.PostLikes.AddAsync(like, cancellationToken);
    }

    public Task RemoveLikeAsync(PostLike like, CancellationToken cancellationToken = default)
    {
        _dbContext.PostLikes.Remove(like);
        return Task.CompletedTask;
    }

    public Task RemovePostAsync(Post post, CancellationToken cancellationToken = default)
    {
        _dbContext.Posts.Remove(post);
        return Task.CompletedTask;
    }

    public Task RemoveCommentAsync(PostComment comment, CancellationToken cancellationToken = default)
    {
        _dbContext.PostComments.Remove(comment);
        return Task.CompletedTask;
    }

    public async Task AddGroupImageAsync(GroupImage image, CancellationToken cancellationToken = default)
    {
        await _dbContext.GroupImages.AddAsync(image, cancellationToken);
    }

    public Task RemoveGroupImageAsync(GroupImage image, CancellationToken cancellationToken = default)
    {
        _dbContext.GroupImages.Remove(image);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<OutstandingPlayerResponse>> GetOutstandingPlayersAsync(CancellationToken cancellationToken = default)
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

    public async Task AddCommunityReportAsync(CommunityReport report, CancellationToken cancellationToken = default)
    {
        await _dbContext.CommunityReports.AddAsync(report, cancellationToken);
    }

    public async Task AddFriendshipAsync(Friendship friendship, CancellationToken cancellationToken = default)
    {
        await _dbContext.Friendships.AddAsync(friendship, cancellationToken);
    }

    public Task RemoveFriendshipAsync(Friendship friendship, CancellationToken cancellationToken = default)
    {
        _dbContext.Friendships.Remove(friendship);
        return Task.CompletedTask;
    }

    public async Task LikeCommentAsync(int commentId, int userId, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId)
            BEGIN
                INSERT INTO [POST_COMMENT_LIKE] ([commentId], [userId]) VALUES (@commentId, @userId);
            END";

        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);

        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId;
        command.Parameters.Add(paramUser);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UnlikeCommentAsync(int commentId, int userId, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId";

        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);

        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId;
        command.Parameters.Add(paramUser);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetCommentLikeCountAsync(int commentId, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId";
        var param = command.CreateParameter();
        param.ParameterName = "@commentId";
        param.Value = commentId;
        command.Parameters.Add(param);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> IsCommentLikedByMeAsync(int commentId, int userId, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId";

        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);

        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId;
        command.Parameters.Add(paramUser);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
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
