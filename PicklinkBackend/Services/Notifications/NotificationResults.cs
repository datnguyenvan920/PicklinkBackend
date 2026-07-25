using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Notifications;

public enum NotificationResultStatus
{
    Success,
    NotFound,
    Unauthorized,
    Forbidden
}

public sealed record NotificationCommandResult(
    NotificationResultStatus Status,
    NotificationLogResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == NotificationResultStatus.Success;

    public static NotificationCommandResult Success(NotificationResponse value) =>
        new(NotificationResultStatus.Success, Value: value as NotificationLogResponse ?? NotificationLogResponse.FromResponse(value));

    public static NotificationCommandResult Deleted() =>
        new(NotificationResultStatus.Success);

    public static NotificationCommandResult NotFound(string message) =>
        new(NotificationResultStatus.NotFound, ErrorMessage: message);
}

public sealed record NotificationListResult(
    NotificationResultStatus Status,
    NotificationListResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == NotificationResultStatus.Success;

    public static NotificationListResult Success(NotificationListResponse value) =>
        new(NotificationResultStatus.Success, Value: value);

    public static NotificationListResult NotFound(string message) =>
        new(NotificationResultStatus.NotFound, ErrorMessage: message);

    public static NotificationListResult InvalidType(string message) =>
        new(NotificationResultStatus.NotFound, ErrorMessage: message);
}
