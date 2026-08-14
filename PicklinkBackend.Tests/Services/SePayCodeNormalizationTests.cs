using System.Text.RegularExpressions;
using Xunit;

namespace PicklinkBackend.Tests.Services;

public class SePayCodeNormalizationTests
{
    [Theory]
    [InlineData("PLG-B36F2B910F674038", "PLG-B36F2B910F674038")]
    [InlineData("PLGB36F2B910F674038", "PLGB36F2B910F674038")]
    [InlineData("chuyen tien PLGB36F2B910F674038 tai tpbank", "PLGB36F2B910F674038")]
    [InlineData("PLG-B36F2B910F674038 chuyen tien", "PLG-B36F2B910F674038")]
    [InlineData("Nguyen Van A chuyen tien BK-10293", "BK-10293")]
    [InlineData("Nguyen Van A chuyen tien BK10293", "BK10293")]
    [InlineData("TK-9988-ABC", "TK9988ABC")]
    public void ExtractedCodesIncludeAnyPrefixBothWithAndWithoutDash(string bankContent, string expectedCode)
    {
        var rawTokens = bankContent.Split(new[] { ' ', ',', '.', ';', ':', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Append(bankContent.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToArray();

        var paymentCodes = rawTokens
            .SelectMany(val =>
            {
                var upper = val.ToUpperInvariant().Trim();
                var withoutDash = upper.Replace("-", "");
                return new[] { upper, withoutDash };
            })
            .Where(val => !string.IsNullOrWhiteSpace(val))
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(expectedCode.ToUpperInvariant(), paymentCodes);
        Assert.Contains(expectedCode.Replace("-", "").ToUpperInvariant(), paymentCodes);
    }
}
