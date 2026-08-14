using System.Text.RegularExpressions;
using Xunit;

namespace PicklinkBackend.Tests.Services;

public class SePayCodeNormalizationTests
{
    [Theory]
    [InlineData("PLG-B36F2B910F674038", "PLG-B36F2B910F674038")]
    [InlineData("PLGB36F2B910F674038", "PLG-B36F2B910F674038")]
    [InlineData("chuyen tien PLGB36F2B910F674038 tai tpbank", "PLG-B36F2B910F674038")]
    [InlineData("PLG-B36F2B910F674038 chuyen tien", "PLG-B36F2B910F674038")]
    public void ExtractedCodesIncludeBothWithAndWithoutDash(string bankContent, string expectedStandardCode)
    {
        var rawMatchedCodes = Regex.Matches(bankContent.ToUpperInvariant(), @"PLG-?[A-Z0-9]{16}")
            .Select(match => match.Value)
            .Append(bankContent.ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToArray();

        var paymentCodes = rawMatchedCodes
            .SelectMany(val =>
            {
                var upper = val.ToUpperInvariant().Trim();
                if (upper.StartsWith("PLG-", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { upper, upper.Replace("-", "") };
                }
                if (upper.StartsWith("PLG", StringComparison.OrdinalIgnoreCase) && upper.Length == 19)
                {
                    return new[] { upper, "PLG-" + upper[3..] };
                }
                return new[] { upper };
            })
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(expectedStandardCode, paymentCodes);
        Assert.Contains(expectedStandardCode.Replace("-", ""), paymentCodes);
    }
}
