using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Community;

public interface ICommunityService
{
    void SetCurrentUserId(int? userId);

    // Groups
    Task<CommunityServiceResult<IReadOnlyList<CommunityGroupResponse>>> Groups(string? query, string? groupType, string? sortBy, int? page, int? pageSize, CancellationToken cancellationToken = default);
    Task<CommunityServiceResult<CommunityGroupResponse>> Group(int groupId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityGroupResponse>> CreateGroup(CreateCommunityGroupRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityGroupResponse>> UpdateGroup(int groupId, UpdateCommunityGroupRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityGroupResponse>> JoinGroup(int groupId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityGroupResponse>> LeaveGroup(int groupId, CancellationToken cancellationToken);

    // Members
    Task<CommunityServiceResult<IReadOnlyList<CommunityMemberResponse>>> Members(int groupId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityMemberResponse>> ApproveMember(int groupId, int memberUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityMemberResponse>> DeclineMember(int groupId, int memberUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityMemberResponse>> BanMember(int groupId, int memberUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult> UnbanMember(int groupId, int memberUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityMemberResponse>> ChangeMemberRole(int groupId, int memberUserId, ChangeRoleRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult> RemoveMember(int groupId, int memberUserId, CancellationToken cancellationToken);

    // Posts
    Task<CommunityServiceResult<IReadOnlyList<CommunityPostResponse>>> Posts(int groupId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> CreatePost(int groupId, CreateCommunityPostRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<IReadOnlyList<CommunityPostResponse>>> GetCommunityPosts(CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> CreateCommunityPost(CreateCommunityPostRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> GetPost(int postId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> UpdatePost(int postId, UpdateCommunityPostRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult> DeletePost(int postId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> ApprovePost(int postId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> ReactToPost(int postId, ReactToPostRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityPostResponse>> RemoveReaction(int postId, CancellationToken cancellationToken);

    // Comments
    Task<CommunityServiceResult<IReadOnlyList<CommunityCommentResponse>>> Comments(int postId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityCommentResponse>> CreateComment(int postId, CreateCommunityCommentRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityCommentResponse>> UpdateComment(int commentId, UpdateCommunityCommentRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult> DeleteComment(int commentId, CancellationToken cancellationToken);
    Task<CommunityServiceResult> LikeComment(int commentId, CancellationToken cancellationToken);
    Task<CommunityServiceResult> UnlikeComment(int commentId, CancellationToken cancellationToken);

    // Messages
    Task<CommunityServiceResult<IReadOnlyList<CommunityMessageResponse>>> Messages(int groupId, int? beforeMessageId, int limit = 8, CancellationToken cancellationToken = default);
    Task<CommunityServiceResult<IReadOnlyList<CommunityMessageResponse>>> PinnedMessages(int groupId, CancellationToken cancellationToken = default);
    Task<CommunityServiceResult<CommunityMessageResponse>> SendMessage(int groupId, SendCommunityMessageRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult> DeleteMessage(int groupId, int messageId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<CommunityMessageResponse>> PinMessage(int groupId, int messageId, bool pin, CancellationToken cancellationToken);

    // Group Images
    Task<CommunityServiceResult<GroupImageResponse>> AddGroupImage(int groupId, AddGroupImageRequest request, CancellationToken cancellationToken);
    Task<CommunityServiceResult> RemoveGroupImage(int groupId, int imageId, CancellationToken cancellationToken);

    // Direct & Friends
    Task<CommunityServiceResult<IReadOnlyList<FriendResponse>>> GetFriends(CancellationToken cancellationToken);
    Task<CommunityServiceResult<IReadOnlyList<PlayerSearchResultResponse>>> SearchPlayers(string? query, int limit, CancellationToken cancellationToken);
    Task<CommunityServiceResult<FriendshipStatusesResponse>> GetFriendshipStatuses(IReadOnlyList<int> targetUserIds, CancellationToken cancellationToken);
    Task<CommunityServiceResult<IReadOnlyList<FriendRequestResponse>>> GetFriendRequests(CancellationToken cancellationToken);
    Task<CommunityServiceResult<FriendshipActionResponse>> SendFriendRequest(int targetUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<FriendshipActionResponse>> AcceptFriendRequest(int targetUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<FriendshipActionResponse>> DeclineFriendRequest(int targetUserId, CancellationToken cancellationToken);
    Task<CommunityServiceResult<FriendshipActionResponse>> RemoveFriend(int targetUserId, CancellationToken cancellationToken);
}
