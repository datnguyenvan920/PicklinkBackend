namespace PicklinkBackend.DTOs;

public sealed record CreateCommunityGroupRequest(
    string? GroupName,
    string? Description,
    string? GroupType,
    string? CoverImageUrl,
    string? ActiveLocation);

public sealed record UpdateCommunityGroupRequest(
    string? GroupName,
    string? Description,
    string? GroupType,
    string? CoverImageUrl,
    string? Rules,
    double? OverallRating,
    int? RatingCount,
    string? ActiveLocation);

public sealed record CreateCommunityPostRequest(
    string? Content,
    IReadOnlyList<string>? MediaUrls);

public sealed record UpdateCommunityPostRequest(string? Content);

public sealed record ReactToPostRequest(string? ReactionType);

public sealed record CreateCommunityCommentRequest(
    string? Content,
    int? ParentCommentId);

public sealed record UpdateCommunityCommentRequest(string? Content);

public sealed record SendCommunityMessageRequest(
    string? Content,
    string? MediaUrl,
    int? ReplyToMessageId);

public sealed record GroupImageResponse(
    int GroupImageId,
    string ImageUrl,
    string? Caption,
    int SortOrder);

public sealed record AddGroupImageRequest(
    string ImageUrl,
    string? Caption,
    int? SortOrder);

public sealed record ChangeRoleRequest(string? Role);

public sealed record CommunityGroupResponse(
    int GroupId,
    string GroupName,
    string? Description,
    string GroupType,
    string? CoverImageUrl,
    DateTime CreatedAt,
    int OwnerPlayerId,
    string OwnerName,
    int MemberCount,
    string? MyRole,
    string? MyStatus,
    int PostCount,
    int MessageCount,
    string? Rules,
    double OverallRating,
    int RatingCount,
    IReadOnlyList<GroupImageResponse> Images,
    string? ActiveLocation,
    int UnreadMessageCount = 0);

public sealed record CommunityMemberResponse(
    int GroupId,
    int UserId,
    string Username,
    string? ProfileImageUrl,
    string Role,
    string Status,
    DateTime JoinedAt);

public sealed record CommunityPostResponse(
    int PostId,
    int? GroupId,
    int AuthorId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string? Content,
    string PostType,
    string Visibility,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> MediaUrls,
    int LikeCount,
    int CommentCount,
    bool LikedByMe,
    string? MyReactionType);

public sealed record CommunityCommentResponse(
    int CommentId,
    int PostId,
    int UserId,
    string Username,
    string? UserAvatarUrl,
    int? ParentCommentId,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int LikeCount,
    bool LikedByMe);

public sealed record CommunityMessageResponse(
    int MessageId,
    int ConversationId,
    int SenderId,
    string SenderName,
    string? SenderAvatarUrl,
    string? Content,
    string MessageType,
    string? MediaUrl,
    int? ReplyToMessageId,
    DateTime SentAt,
    bool IsMine,
    bool IsPinned);

public sealed record OutstandingPlayerResponse(
    int UserId,
    string Name,
    string Level,
    string? Avatar);

public sealed record DirectConversationResponse(
    int ConversationId,
    int OtherUserId,
    string OtherUsername,
    string? OtherProfileImageUrl,
    string OtherSkillLevel,
    DateTime LastMessageAt,
    string LastMessage,
    int UnreadMessageCount = 0);

public sealed record UnreadMessageSenderCountResponse(int Count);

public sealed record FriendResponse(
    int UserId,
    string Username,
    string? ProfileImageUrl);
