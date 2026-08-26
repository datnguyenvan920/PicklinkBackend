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

        var htmlBody = BuildPasswordResetHtml(recipientName, resetCode);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail.Trim(), _options.FromName.Trim(), Encoding.UTF8),
            Subject = "Mã xác thực đặt lại mật khẩu - Picklink",
            SubjectEncoding = Encoding.UTF8,
            Body = htmlBody,
            BodyEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };

        var htmlView = AlternateView.CreateAlternateViewFromString(
            htmlBody,
            Encoding.UTF8,
            System.Net.Mime.MediaTypeNames.Text.Html);
        message.AlternateViews.Add(htmlView);

        message.To.Add(new MailAddress(recipientEmail.Trim(), recipientName.Trim(), Encoding.UTF8));

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

    private static string BuildPasswordResetHtml(string recipientName, string resetCode)
    {
        var greetingName = string.IsNullOrWhiteSpace(recipientName)
            ? "b&#7841;n"
            : WebUtility.HtmlEncode(recipientName.Trim());

        var encodedCode = WebUtility.HtmlEncode(resetCode.Trim());
        var year = DateTime.UtcNow.Year;

        return $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="UTF-8">
              <meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>M&#227; &#273;&#7863;t l&#7841;i m&#7853;t kh&#7849;u Picklink</title>
            </head>
            <body style="margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color: #f1f5f9; padding: 40px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" style="max-width: 560px; background-color: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.08), 0 8px 10px -6px rgba(0, 0, 0, 0.03); border: 1px solid #e2e8f0;" cellspacing="0" cellpadding="0" border="0">
                      
                      <!-- Header Gradient Banner -->
                      <tr>
                        <td style="background: linear-gradient(135deg, #059669 0%, #0d9488 50%, #0284c7 100%); padding: 36px 32px 30px; text-align: center;">
                          <div style="display: inline-block; font-size: 28px; font-weight: 900; letter-spacing: 2px; color: #ffffff; text-transform: uppercase;">
                            PICK<span style="color: #6ee7b7;">LINK</span>
                          </div>
                          <div style="font-size: 13px; color: rgba(255, 255, 255, 0.88); margin-top: 6px; font-weight: 500; letter-spacing: 0.5px;">
                            N&#7873;n t&#7843;ng k&#7871;t n&#7889;i v&#224; &#273;&#7863;t s&#226;n Pickleball
                          </div>
                        </td>
                      </tr>

                      <!-- Body Content -->
                      <tr>
                        <td style="padding: 36px 32px 28px; color: #334155; font-size: 15px; line-height: 1.6;">
                          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 700; color: #0f172a;">
                            Y&#234;u c&#7847;u &#273;&#7863;t l&#7841;i m&#7853;t kh&#7849;u
                          </h2>

                          <p style="margin: 0 0 16px; color: #475569;">
                            Xin ch&#224;o <strong style="color: #0f172a;">{{greetingName}}</strong>,
                          </p>

                          <p style="margin: 0 0 24px; color: #475569;">
                            Ch&#250;ng t&#244;i nh&#7853;n &#273;&#432;&#7907;c y&#234;u c&#7847;u &#273;&#7863;t l&#7841;i m&#7853;t kh&#7849;u cho t&#224;i kho&#7843;n Picklink c&#7911;a b&#7841;n. H&#227;y s&#7917; d&#7909;ng m&#227; x&#225;c th&#7921;c (OTP) b&#234;n d&#432;&#7899;i &#273;&#7875; ti&#7871;n h&#224;nh t&#7841;o m&#7853;t kh&#7849;u m&#7899;i:
                          </p>

                          <!-- OTP Code Box -->
                          <div style="background: #f8fafc; border: 2px dashed #cbd5e1; border-radius: 12px; padding: 22px 16px; text-align: center; margin: 24px 0 28px;">
                            <div style="font-size: 12px; font-weight: 700; color: #64748b; text-transform: uppercase; letter-spacing: 1.5px; margin-bottom: 8px;">
                              M&#227; x&#225;c th&#7921;c c&#7911;a b&#7841;n
                            </div>
                            <div style="font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #0f172a; font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, Courier, monospace;">
                              {{encodedCode}}
                            </div>
                          </div>

                          <!-- Notice Box -->
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color: #fefce8; border-left: 4px solid #eab308; border-radius: 6px; padding: 14px 16px; margin-bottom: 24px;">
                            <tr>
                              <td style="font-size: 13px; color: #854d0e; line-height: 1.5;">
                                <strong>L&#432;u &#253;:</strong> M&#227; n&#224;y c&#243; hi&#7879;u l&#7921;c trong <strong>15 ph&#250;t</strong>. Tuy&#7879;t &#273;&#7889;i kh&#244;ng chia s&#7867; m&#227; n&#224;y v&#7889;i b&#7845;t k&#7923; ai &#273;&#7875; &#273;&#7843;m b&#7843;o an to&#224;n t&#224;i kho&#7843;n.
                              </td>
                            </tr>
                          </table>

                          <p style="margin: 0; font-size: 13px; color: #94a3b8; line-height: 1.5;">
                            N&#7871;u b&#7841;n kh&#244;ng y&#234;u c&#7847;u &#273;&#7893;i m&#7853;t kh&#7849;u, b&#7841;n c&#243; th&#7875; y&#234;n t&#226;m b&#7887; qua email n&#224;y. T&#224;i kho&#7843;n c&#7911;a b&#7841;n v&#7851;n an to&#224;n.
                          </p>
                        </td>
                      </tr>

                      <!-- Divider -->
                      <tr>
                        <td style="padding: 0 32px;">
                          <div style="border-top: 1px solid #f1f5f9;"></div>
                        </td>
                      </tr>

                      <!-- Footer -->
                      <tr>
                        <td style="padding: 24px 32px 32px; text-align: center; background-color: #ffffff;">
                          <p style="margin: 0 0 6px; font-size: 12px; color: #94a3b8;">
                            &copy; {{year}} Picklink. To&#224;n quy&#7873;n &#273;&#432;&#7907;c b&#7843;o l&#432;u.
                          </p>
                          <p style="margin: 0; font-size: 11px; color: #cbd5e1;">
                            &#272;&#226;y l&#224; email t&#7921; &#273;&#7897;ng t&#7915; h&#7879; th&#7889;ng. Vui l&#242;ng kh&#244;ng tr&#7843; l&#7901;i tr&#7921;c ti&#7871;p email n&#224;y.
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
