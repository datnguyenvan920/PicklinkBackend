namespace PicklinkBackend.Startup;

/// <summary>
/// Vietnamese replacements for the English titles ASP.NET Core puts on ProblemDetails
/// responses. The frontend falls back to <c>title</c> when a handler did not supply its
/// own <c>message</c>, so these strings are shown to real users.
/// </summary>
internal static class ProblemTitles
{
    internal static string ForStatus(int statusCode) => statusCode switch
    {
        400 => "Yêu cầu không hợp lệ.",
        401 => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
        403 => "Tài khoản không có quyền thực hiện thao tác này.",
        404 => "Không tìm thấy dữ liệu được yêu cầu.",
        405 => "Phương thức không được hỗ trợ.",
        409 => "Dữ liệu vừa thay đổi. Vui lòng tải lại và thử lại.",
        413 => "Dữ liệu gửi lên quá lớn.",
        415 => "Định dạng dữ liệu không được hỗ trợ.",
        422 => "Dữ liệu không hợp lệ.",
        429 => "Bạn thao tác quá nhanh. Vui lòng chờ một lúc rồi thử lại.",
        503 => "Hệ thống đang bảo trì. Vui lòng thử lại sau.",
        _ when statusCode >= 500 => "Máy chủ đang gặp sự cố. Vui lòng thử lại sau.",
        _ => "Yêu cầu không thành công."
    };
}
