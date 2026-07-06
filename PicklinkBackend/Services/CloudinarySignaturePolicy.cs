namespace PicklinkBackend.Services;

public static class CloudinarySignaturePolicy
{
    private const string UploadFolder = "picklink_clubs";

    public static bool TryValidate(
        IReadOnlyDictionary<string, string>? parameters,
        out Dictionary<string, string> validated)
    {
        validated = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters is null || parameters.Count != 1)
        {
            return false;
        }

        if (parameters.TryGetValue("folder", out var folder) &&
            string.Equals(folder, UploadFolder, StringComparison.Ordinal))
        {
            validated["folder"] = UploadFolder;
            return true;
        }

        return false;
    }
}
