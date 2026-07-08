using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace PicklinkBackend.Services.Infrastructure;

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
            Subject = "MÃƒÂ£ Ã„â€˜Ã¡ÂºÂ·t lÃ¡ÂºÂ¡i mÃ¡ÂºÂ­t khÃ¡ÂºÂ©u Picklink",
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
            ? "bÃ¡ÂºÂ¡n"
            : recipientName.Trim();

        return $"""
            Xin chÃƒÂ o {greetingName},

            MÃƒÂ£ Ã„â€˜Ã¡ÂºÂ·t lÃ¡ÂºÂ¡i mÃ¡ÂºÂ­t khÃ¡ÂºÂ©u Picklink cÃ¡Â»Â§a bÃ¡ÂºÂ¡n lÃƒÂ : {resetCode}

            MÃƒÂ£ nÃƒÂ y cÃƒÂ³ hiÃ¡Â»â€¡u lÃ¡Â»Â±c trong 15 phÃƒÂºt. NÃ¡ÂºÂ¿u bÃ¡ÂºÂ¡n khÃƒÂ´ng yÃƒÂªu cÃ¡ÂºÂ§u Ã„â€˜Ã¡ÂºÂ·t lÃ¡ÂºÂ¡i mÃ¡ÂºÂ­t khÃ¡ÂºÂ©u, vui lÃƒÂ²ng bÃ¡Â»Â qua email nÃƒÂ y.

            Picklink
            """;
    }
}
