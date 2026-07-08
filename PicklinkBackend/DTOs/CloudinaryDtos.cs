namespace PicklinkBackend.DTOs;

public class SignatureRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
}

public class SignatureResponse
{
    public string Signature { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public string ApiKey { get; set; } = null!;
    public string CloudName { get; set; } = null!;
}