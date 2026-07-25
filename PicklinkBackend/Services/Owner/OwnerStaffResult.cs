using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Owner;

public enum OwnerStaffResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record OwnerStaffListResult(
    OwnerStaffResultStatus Status,
    List<OwnerStaffResponse>? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == OwnerStaffResultStatus.Success;

    public static OwnerStaffListResult Success(IEnumerable<OwnerStaffResponse> value) =>
        new(OwnerStaffResultStatus.Success, Value: value.ToList());

    public static OwnerStaffListResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, ErrorMessage: "Vui long dang nhap.");
}

public sealed record OwnerStaffMutationResult(
    OwnerStaffResultStatus Status,
    OwnerStaffAccountResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == OwnerStaffResultStatus.Success;

    public static OwnerStaffMutationResult Success(OwnerStaffAccountResponse value) =>
        new(OwnerStaffResultStatus.Success, Value: value);

    public static OwnerStaffMutationResult BadRequest(string message) =>
        new(OwnerStaffResultStatus.BadRequest, ErrorMessage: message);

    public static OwnerStaffMutationResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, ErrorMessage: "Vui long dang nhap.");

    public static OwnerStaffMutationResult Forbidden(string message) =>
        new(OwnerStaffResultStatus.Forbidden, ErrorMessage: message);

    public static OwnerStaffMutationResult NotFound(string message) =>
        new(OwnerStaffResultStatus.NotFound, ErrorMessage: message);

    public static OwnerStaffMutationResult Conflict(string message) =>
        new(OwnerStaffResultStatus.Conflict, ErrorMessage: message);
}

public sealed record OwnerCheckInHistoryResult(
    OwnerStaffResultStatus Status,
    PaginatedResponse<OwnerCheckInHistoryResponse>? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == OwnerStaffResultStatus.Success;

    public static OwnerCheckInHistoryResult Success(PaginatedResponse<OwnerCheckInHistoryResponse> value) =>
        new(OwnerStaffResultStatus.Success, Value: value);

    public static OwnerCheckInHistoryResult BadRequest(string message) =>
        new(OwnerStaffResultStatus.BadRequest, ErrorMessage: message);

    public static OwnerCheckInHistoryResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, ErrorMessage: "Vui long dang nhap.");

    public static OwnerCheckInHistoryResult Forbidden(string message) =>
        new(OwnerStaffResultStatus.Forbidden, ErrorMessage: message);

    public static OwnerCheckInHistoryResult NotFound(string message) =>
        new(OwnerStaffResultStatus.NotFound, ErrorMessage: message);

    public static OwnerCheckInHistoryResult Conflict(string message) =>
        new(OwnerStaffResultStatus.Conflict, ErrorMessage: message);
}
