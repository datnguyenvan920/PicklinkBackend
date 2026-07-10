using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("posts/{postId:int}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommunityCommentResponse>>> Comments(int postId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Comments(postId, cancellationToken));
    }

    [HttpPost("posts/{postId:int}/comments")]
    public async Task<ActionResult<CommunityCommentResponse>> CreateComment(int postId, CreateCommunityCommentRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.CreateComment(postId, request, cancellationToken));
    }

    [HttpPut("comments/{commentId:int}")]
    public async Task<ActionResult<CommunityCommentResponse>> UpdateComment(int commentId, UpdateCommunityCommentRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.UpdateComment(commentId, request, cancellationToken));
    }

    [HttpDelete("comments/{commentId:int}")]
    public async Task<ActionResult> DeleteComment(int commentId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.DeleteComment(commentId, cancellationToken));
    }

    [HttpPost("comments/{commentId:int}/like")]
    public async Task<ActionResult> LikeComment(int commentId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.LikeComment(commentId, cancellationToken));
    }

    [HttpDelete("comments/{commentId:int}/like")]
    public async Task<ActionResult> UnlikeComment(int commentId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.UnlikeComment(commentId, cancellationToken));
    }
}