using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Email này chưa được đăng ký. Vui lòng đăng ký tài khoản trước." });
        }

        var resetToken = GeneratePasswordResetToken();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(15);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var activeTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.UsedAt == null &&
                            token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }

        var passwordResetToken = new PasswordResetToken
        {
            UserId = user.UserId,
            TokenHash = HashPasswordResetToken(resetToken),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _dbContext.PasswordResetTokens.Add(passwordResetToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailSender.SendPasswordResetCodeAsync(
                user.Email,
                user.Username,
                resetToken,
                expiresAt,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Password reset email is not configured.");

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Chức năng gửi email chưa được cấu hình. Vui lòng cấu hình SMTP cho máy chủ."
            });
        }
        catch (SmtpException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Could not send password reset email to {Email}.", user.Email);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng thử lại sau."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Unexpected error while sending password reset email to {Email}.", user.Email);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng kiểm tra cấu hình email và thử lại."
            });
        }

        await transaction.CommitAsync(cancellationToken);

        return Ok(new ForgotPasswordResponse
        {
            Message = "Mã đặt lại mật khẩu đã được gửi qua email. Vui lòng kiểm tra hộp thư và dùng mã trong vòng 15 phút.",
            ExpiresAt = expiresAt
        });
    }

    [HttpPost("verify-reset-code")]
    public async Task<ActionResult> VerifyResetCode(
        VerifyPasswordResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var isValid = await _dbContext.PasswordResetTokens
            .AsNoTracking()
            .AnyAsync(token =>
                token.User.Email == email &&
                token.TokenHash == tokenHash &&
                token.UsedAt == null &&
                token.ExpiresAt > now,
                cancellationToken);

        if (!isValid)
        {
            return BadRequest(new { message = "Mã xác thực không hợp lệ hoặc đã hết hạn." });
        }

        return Ok(new { message = "Mã xác thực hợp lệ." });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Email này chưa được đăng ký. Vui lòng đăng ký tài khoản trước." });
        }

        var resetToken = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.TokenHash == tokenHash &&
                            token.UsedAt == null)
            .OrderByDescending(token => token.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now)
        {
            return BadRequest(new { message = "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        resetToken.UsedAt = now;

        var otherActiveTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.ResetTokenId != resetToken.ResetTokenId &&
                            token.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in otherActiveTokens)
        {
            activeToken.UsedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới." });
    }

    private static string GeneratePasswordResetToken()
    {
        return RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
    }

    private static string HashPasswordResetToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
