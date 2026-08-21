using PicklinkBackend.Services.Matches;

namespace PicklinkBackend.Tests.Policies;

public class MatchRoomLifecyclePolicyTests
{
    [Theory]
    [InlineData("Approved", true)]
    [InlineData("Accepted", true)]
    [InlineData("Pending", false)]
    [InlineData("Invited", false)]
    [InlineData("Left", false)]
    [InlineData("Removed", false)]
    public void OnlyApprovedOrAcceptedPlayersCountAsRoomMembers(string status, bool expected)
    {
        Assert.Equal(expected, MatchRoomLifecyclePolicy.IsRoomMemberStatus(status));
    }

    [Theory]
    [InlineData(1, 2, "Recruiting")]
    [InlineData(2, 2, "ReadyToBook")]
    [InlineData(4, 4, "ReadyToBook")]
    public void VisibleRoomStatusDependsOnlyOnMemberCapacity(int members, int required, string expected)
    {
        Assert.Equal(expected, MatchRoomLifecyclePolicy.RoomStatusFor(members, required));
    }

    [Theory]
    [InlineData("Expired", 4, 4, "ReadyToBook")]
    [InlineData("Expired", 3, 4, "Recruiting")]
    [InlineData("Completed", 4, 4, "Completed")]
    public void LegacyExpiredRoomsRecoverFromTheirCurrentRoster(
        string currentStatus,
        int members,
        int required,
        string expected)
    {
        Assert.Equal(
            expected,
            MatchRoomLifecyclePolicy.EffectiveRoomStatusFor(currentStatus, members, required));
    }
}
