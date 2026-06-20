using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace PicklinkBackend.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendPasswordResetCodeAsync(
        string recipientEmail,
        string recipientName,
        string resetCode,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail.Trim(), _options.FromName.Trim()),
            Subject = "Mã đặt lại mật khẩu Picklink",
            SubjectEncoding = Encoding.UTF8,
            Body = BuildPasswordResetBody(recipientName, resetCode),
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        message.To.Add(new MailAddress(recipientEmail.Trim(), recipientName.Trim()));

        using var smtpClient = new SmtpClient(_options.Smtp.Host.Trim(), _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
        {
            smtpClient.Credentials = new NetworkCredential(
                _options.Smtp.Username.Trim(),
                _options.Smtp.Password);
        }

        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("Email:FromEmail is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Smtp.Host))
        {
            throw new InvalidOperationException("Email:Smtp:Host is not configured.");
        }

        if (_options.Smtp.Port <= 0)
        {
            throw new InvalidOperationException("Email:Smtp:Port is not configured.");
        }
    }

    private static string BuildPasswordResetBody(string recipientName, string resetCode)
    {
        var greetingName = string.IsNullOrWhiteSpace(recipientName)
            ? "bạn"
            : recipientName.Trim();

        return $"""
            Xin chào {greetingName},

            Mã đặt lại mật khẩu Picklink của bạn là: {resetCode}

            Mã này có hiệu lực trong 15 phút. Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.

            Picklink
            """;
    }
}
