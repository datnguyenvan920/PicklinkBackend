namespace PicklinkBackend.Services.Matches;

public static class MatchRoomLifecyclePolicy
{
    public const string Recruiting = "Recruiting";
    public const string ReadyToBook = "ReadyToBook";

    public static bool IsRoomMemberStatus(string? participantStatus) =>
        participantStatus is "Approved" or "Accepted";

    public static string RoomStatusFor(int memberCount, int requiredPlayerCount) =>
        memberCount >= Math.Max(1, requiredPlayerCount)
            ? ReadyToBook
            : Recruiting;
}
