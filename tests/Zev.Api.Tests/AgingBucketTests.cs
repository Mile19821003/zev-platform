using Xunit;
using Zev.Api.Reporting;

namespace Zev.Api.Tests;

public sealed class AgingBucketTests
{
    [Theory]
    [InlineData(-1, "NotDue")]
    [InlineData(0, "NotDue")]
    [InlineData(1, "Days1To30")]
    [InlineData(30, "Days1To30")]
    [InlineData(31, "Days31To60")]
    [InlineData(60, "Days31To60")]
    [InlineData(61, "Days61To90")]
    [InlineData(91, "Days91To180")]
    [InlineData(181, "Days181To365")]
    [InlineData(366, "Over365Days")]
    public void ClassifiesAgingBoundaries(int days, string expected)
    {
        var asOf = new DateOnly(2026, 9, 27);
        Assert.Equal(expected, AgingBucket.For(asOf.AddDays(-days), asOf));
    }
}
