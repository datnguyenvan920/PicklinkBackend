using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface ICommunityRepository
{
    IQueryable<SocialGroup> SocialGroups { get; }
    IQueryable<GroupMember> GroupMembers { get; }
    IQueryable<Post> GroupPosts { get; }
    IQueryable<PostComment> GroupComments { get; }
    IQueryable<PostLike> PostLikes { get; }
    IQueryable<Conversation> Conversations { get; }
    IQueryable<ConversationParticipant> ConversationParticipants { get; }
    IQueryable<Message> Messages { get; }
    IQueryable<User> Users { get; }
    IQueryable<Player> Players { get; }
    IQueryable<MatchParticipant> MatchParticipants { get; }
    IQueryable<MatchSlotReplacementRequest> MatchSlotReplacementRequests { get; }
    IQueryable<CommunityReport> CommunityReports { get; }
    IQueryable<Friendship> Friendships { get; }
    IQueryable<GroupImage> GroupImages { get; }

    Task<SocialGroup?> GetGroupByIdAsync(int groupId, CancellationToken cancellationToken = default);
    Task<Conversation?> GetConversationByIdAsync(int conversationId, CancellationToken cancellationToken = default);
    Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default);
    Task AddGroupAsync(SocialGroup group, CancellationToken cancellationToken = default);
    Task AddPostAsync(Post post, CancellationToken cancellationToken = default);
    Task AddCommentAsync(PostComment comment, CancellationToken cancellationToken = default);
    Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(GroupMember member, CancellationToken cancellationToken = default);
    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken = default);
    Task AddLikeAsync(PostLike like, CancellationToken cancellationToken = default);
    Task RemoveLikeAsync(PostLike like, CancellationToken cancellationToken = default);
    Task RemovePostAsync(Post post, CancellationToken cancellationToken = default);
    Task RemoveCommentAsync(PostComment comment, CancellationToken cancellationToken = default);
    Task AddGroupImageAsync(GroupImage image, CancellationToken cancellationToken = default);
    Task RemoveGroupImageAsync(GroupImage image, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutstandingPlayerResponse>> GetOutstandingPlayersAsync(CancellationToken cancellationToken = default);
    Task AddCommunityReportAsync(CommunityReport report, CancellationToken cancellationToken = default);
    Task LikeCommentAsync(int commentId, int userId, CancellationToken cancellationToken = default);
    Task UnlikeCommentAsync(int commentId, int userId, CancellationToken cancellationToken = default);
    Task<int> GetCommentLikeCountAsync(int commentId, CancellationToken cancellationToken = default);
    Task<bool> IsCommentLikedByMeAsync(int commentId, int userId, CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
