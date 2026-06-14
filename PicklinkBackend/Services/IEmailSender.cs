namespace PicklinkBackend.Services;

public interface IEmailSender
{
    Task SendPasswordResetCodeAsync(
        string recipientEmail,
        string recipientName,
        string resetCode,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
}
