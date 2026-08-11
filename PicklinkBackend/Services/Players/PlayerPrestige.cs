namespace PicklinkBackend.Services.Players;

public static class PlayerPrestige
{
    public const double InitialScore = 5d;

    public static double Average(int reviewScoreTotal, int reviewCount) =>
        Math.Round((InitialScore + reviewScoreTotal) / (reviewCount + 1), 1, MidpointRounding.AwayFromZero);
}
