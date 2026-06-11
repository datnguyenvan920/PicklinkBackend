using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IGoogleAuthService _googleAuthService;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IGoogleAuthService googleAuthService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _googleAuthService = googleAuthService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(user => user.Email == email))
        {
            return Conflict(new { message = "Email is already registered." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Username == username))
        {
            return Conflict(new { message = "Username is already registered." });
        }

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            UserType = "User",
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
                ? null
                : request.ProfileImageUrl.Trim()
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        return Ok(CreateAuthResponse(user));
    }

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
            return Unauthorized(new { message = "Google token is invalid." });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Username = await CreateUniqueUsernameAsync(googleUser.Name, email, cancellationToken),
                Email = email,
                PasswordHash = _passwordHasher.Hash(CreateExternalLoginPassword()),
                UserType = "User",
                ProfileImageUrl = string.IsNullOrWhiteSpace(googleUser.Picture)
                    ? null
                    : googleUser.Picture.Trim()
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
                 !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(CreateAuthResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(UserResponse.FromUser(user));
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var tokenResult = _jwtTokenService.GenerateToken(user);

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAt = tokenResult.ExpiresAt,
            User = UserResponse.FromUser(user)
        };
    }

    private async Task<string> CreateUniqueUsernameAsync(
        string? preferredName,
        string email,
        CancellationToken cancellationToken)
    {
        var source = string.IsNullOrWhiteSpace(preferredName)
            ? email.Split('@')[0]
            : preferredName.Trim();

        var baseUsername = Regex.Replace(source.ToLowerInvariant(), "[^a-z0-9_]", string.Empty);
        if (baseUsername.Length < 3)
        {
            baseUsername = "googleuser";
        }

        baseUsername = baseUsername[..Math.Min(baseUsername.Length, 90)];
        var username = baseUsername;
        var suffix = 1;

        while (await _dbContext.Users.AnyAsync(user => user.Username == username, cancellationToken))
        {
            suffix++;
            var suffixText = suffix.ToString();
            var prefixLength = Math.Min(baseUsername.Length, 100 - suffixText.Length);
            username = baseUsername[..prefixLength] + suffixText;
        }

        return username;
    }

    private static string CreateExternalLoginPassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
