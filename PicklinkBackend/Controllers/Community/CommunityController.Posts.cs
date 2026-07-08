using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("groups/{groupId:int}/posts")]
    public async Task<ActionResult<IReadOnlyList<CommunityPostResponse>>> Posts(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Posts(groupId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/posts")]
    public async Task<ActionResult<CommunityPostResponse>> CreatePost(int groupId, CreateCommunityPostRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.CreatePost(groupId, request, cancellationToken));
    }

    [HttpGet("posts")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CommunityPostResponse>>> GetCommunityPosts(CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.GetCommunityPosts(cancellationToken));
    }

    [HttpPost("posts")]
    public async Task<ActionResult<CommunityPostResponse>> CreateCommunityPost(CreateCommunityPostRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.CreateCommunityPost(request, cancellationToken));
    }

    [HttpGet("posts/{postId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<CommunityPostResponse>> GetPost(int postId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.GetPost(postId, cancellationToken));
    }

    [HttpPut("posts/{postId:int}")]
    public async Task<ActionResult<CommunityPostResponse>> UpdatePost(int postId, UpdateCommunityPostRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.UpdatePost(postId, request, cancellationToken));
    }

    [HttpDelete("posts/{postId:int}")]
    public async Task<ActionResult> DeletePost(int postId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.DeletePost(postId, cancellationToken));
    }

    [HttpPost("posts/{postId:int}/approve")]
    public async Task<ActionResult<CommunityPostResponse>> ApprovePost(int postId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.ApprovePost(postId, cancellationToken));
    }

    [HttpPost("posts/{postId:int}/reaction")]
    public async Task<ActionResult<CommunityPostResponse>> ReactToPost(int postId, ReactToPostRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.ReactToPost(postId, request, cancellationToken));
    }

    [HttpDelete("posts/{postId:int}/reaction")]
    public async Task<ActionResult<CommunityPostResponse>> RemoveReaction(int postId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.RemoveReaction(postId, cancellationToken));
    }
}