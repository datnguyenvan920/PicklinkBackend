using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Community;

public enum ReportSubmissionResultStatus
{
    Success,
    BadRequest,
    Unauthorized
}

public sealed record ReportSubmissionResult(
    bool IsSuccess,
    ReportSubmissionResponse? Value = null,
    string? ErrorMessage = null,
    bool IsUnauthorized = false)
{
    public ReportSubmissionResponse? Report => Value;

    public ReportSubmissionResultStatus Status => IsSuccess
        ? ReportSubmissionResultStatus.Success
        : (IsUnauthorized ? ReportSubmissionResultStatus.Unauthorized : ReportSubmissionResultStatus.BadRequest);

    public static ReportSubmissionResult Success(ReportSubmissionResponse value) =>
        new(true, Value: value);

    public static ReportSubmissionResult BadRequest(string message) =>
        new(false, ErrorMessage: message);

    public static ReportSubmissionResult Unauthorized() =>
        new(false, IsUnauthorized: true);
}
