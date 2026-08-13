using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public enum AdminResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record AdminUserListResult(
    AdminResultStatus Status,
    PaginatedResponse<AdminUserResponse>? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminUserListResult Success(PaginatedResponse<AdminUserResponse> value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminUserListResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminUserListResult InvalidRole(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);
}

public sealed record AdminUserLockResult(
    AdminResultStatus Status,
    AdminUserResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminUserLockResult Success(AdminUserResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminUserLockResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminUserLockResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminUserLockResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminUserLockResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record AdminVenueApprovalResult(
    AdminResultStatus Status,
    AdminVenueResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminVenueApprovalResult Success(AdminVenueResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminVenueApprovalResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminVenueApprovalResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminVenueApprovalResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminVenueApprovalResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record AdminVenueListResult(
    AdminResultStatus Status,
    PaginatedResponse<AdminVenueResponse>? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminVenueListResult Success(PaginatedResponse<AdminVenueResponse> value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminVenueListResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminVenueListResult InvalidStatus(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);
}

public sealed record AdminListingFeePaymentListResult(
    AdminResultStatus Status,
    PaginatedResponse<AdminListingFeePaymentResponse>? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminListingFeePaymentListResult Success(PaginatedResponse<AdminListingFeePaymentResponse> value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminListingFeePaymentListResult InvalidStatus(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);
}

public sealed record AdminListingFeePaymentReviewResult(
    AdminResultStatus Status,
    AdminListingFeePaymentResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminListingFeePaymentReviewResult Success(AdminListingFeePaymentResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminListingFeePaymentReviewResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminListingFeePaymentReviewResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminListingFeePaymentReviewResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminListingFeePaymentReviewResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record ListingFeeSettingUpdateResult(
    AdminResultStatus Status,
    AdminListingFeeSettingResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static ListingFeeSettingUpdateResult Success(AdminListingFeeSettingResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static ListingFeeSettingUpdateResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static ListingFeeSettingUpdateResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");
}

public sealed record AdminReportReviewResult(
    AdminResultStatus Status,
    AdminReportResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminReportReviewResult Success(AdminReportResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminReportReviewResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminReportReviewResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminReportReviewResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminReportReviewResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record AdminReviewModerationResult(
    AdminResultStatus Status,
    AdminVenueReviewResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminReviewModerationResult Success(AdminVenueReviewResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminReviewModerationResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminReviewModerationResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminReviewModerationResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record AdminSettingUpdateResult(
    AdminResultStatus Status,
    AdminSettingResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminSettingUpdateResult Success(AdminSettingResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminSettingUpdateResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminSettingUpdateResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminSettingUpdateResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);
}

public sealed record AdminPostModerationResult(
    AdminResultStatus Status,
    AdminPostResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminPostModerationResult Success(AdminPostResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminPostModerationResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminPostModerationResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);
}

public sealed record AdminPostDeleteResult(
    AdminResultStatus Status,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminPostDeleteResult Success() =>
        new(AdminResultStatus.Success);

    public static AdminPostDeleteResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminPostDeleteResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);
}

public sealed record AdminClubModerationResult(
    AdminResultStatus Status,
    AdminClubResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminClubModerationResult Success(AdminClubResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminClubModerationResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminClubModerationResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);
}

public sealed record AdminBookingCancelResult(
    AdminResultStatus Status,
    AdminBookingSummaryResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == AdminResultStatus.Success;

    public static AdminBookingCancelResult Success(AdminBookingSummaryResponse value) =>
        new(AdminResultStatus.Success, Value: value);

    public static AdminBookingCancelResult BadRequest(string message) =>
        new(AdminResultStatus.BadRequest, ErrorMessage: message);

    public static AdminBookingCancelResult Unauthorized() =>
        new(AdminResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static AdminBookingCancelResult NotFound(string message) =>
        new(AdminResultStatus.NotFound, ErrorMessage: message);

    public static AdminBookingCancelResult Conflict(string message) =>
        new(AdminResultStatus.Conflict, ErrorMessage: message);
}

public sealed record PlatformSettingDefinition(
    string Group,
    string DefaultValue,
    string Description,
    int MinValue,
    int MaxValue);
