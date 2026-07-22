namespace PicklinkBackend.Services.Venues;

public static class CloudinarySignaturePolicy
{
    private static readonly HashSet<string> AllowedUploadFolders = new(StringComparer.Ordinal)
    {
        "picklink_clubs",
        "picklink_avatars",
        "picklink_posts",
        "picklink_messages"
    };

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
            !string.IsNullOrWhiteSpace(folder) &&
            AllowedUploadFolders.Contains(folder))
        {
            validated["folder"] = folder;
            return true;
        }

        return false;
    }
}
