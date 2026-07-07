using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        GoogleUserInfo googleUser;
        try
        {
            googleUser = await _googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return Unauthorized(new { message = "Phiên đăng nhập Google không hợp lệ. Vui lòng thử lại." });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                title: "Cấu hình đăng nhập Google chưa hợp lệ.",
                detail: exception.Message);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            return NotFound(new
            {
                message = "Email Google này chưa được đăng ký. Vui lòng đăng ký tài khoản trước."
            });
        }

        if (user.IsLocked)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
            !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("google/register")]
    public async Task<ActionResult<AuthResponse>> GoogleRegister(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        GoogleUserInfo googleUser;
        try
        {
            googleUser = await _googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return Unauthorized(new { message = "Phiên đăng ký Google không hợp lệ. Vui lòng thử lại." });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                title: "Cấu hình đăng nhập Google chưa hợp lệ.",
                detail: exception.Message);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new
            {
                message = "Email Google này đã được đăng ký. Vui lòng chọn đăng nhập bằng Google."
            });
        }

        var user = new User
        {
            Username = await CreateUniqueGoogleUsernameAsync(googleUser, cancellationToken),
            Email = email,
            PasswordHash = _passwordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            UserType = "Player",
            ProfileImageUrl = string.IsNullOrWhiteSpace(googleUser.Picture)
                ? null
                : googleUser.Picture.Trim()
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.Players.Add(new Player
        {
            UserId = user.UserId,
            SkillLevel = 1,
            Prestige = 100
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(CreateAuthResponse(user));
    }

    private async Task<string> CreateUniqueGoogleUsernameAsync(
        GoogleUserInfo googleUser,
        CancellationToken cancellationToken)
    {
        var baseName = string.IsNullOrWhiteSpace(googleUser.Name)
            ? googleUser.Email.Split('@')[0]
            : googleUser.Name.Trim();

        if (baseName.Length < 3)
        {
            baseName = $"Picklink {baseName}".Trim();
        }

        baseName = baseName[..Math.Min(baseName.Length, 90)];
        var candidate = baseName;

        while (await _dbContext.Users.AnyAsync(user => user.Username == candidate, cancellationToken))
        {
            candidate = $"{baseName}-{RandomNumberGenerator.GetInt32(1000, 10_000)}";
        }

        return candidate;
    }
}
