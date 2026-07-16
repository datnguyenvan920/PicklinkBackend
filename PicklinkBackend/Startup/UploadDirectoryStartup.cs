namespace PicklinkBackend.Startup;

internal static class UploadDirectoryStartup
{
    internal static void EnsureUploadDirectories(this WebApplicationBuilder builder)
    {
        var webRootPath = builder.Environment.WebRootPath
            ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");

        Directory.CreateDirectory(Path.Combine(webRootPath, "uploads", "avatars"));
        Directory.CreateDirectory(Path.Combine(webRootPath, "uploads", "venues"));
        Directory.CreateDirectory(Path.Combine(webRootPath, "uploads", "payment-receipts"));
        Directory.CreateDirectory(Path.Combine(webRootPath, "uploads", "group-covers"));
    }
}
