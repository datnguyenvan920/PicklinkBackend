namespace PicklinkBackend.Services;

public class EmailOptions
{
    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = "PickleMatch";

    public SmtpOptions Smtp { get; set; } = new();
}

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool EnableSsl { get; set; } = true;
}
