using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;

namespace EtwSnap.WpaPlugin.Tests.Parsing;

public sealed class EtwSnapTraceParserTests
{
    [Theory]
    [InlineData(10_000_000, 100_000_000, 11_000_000, 1_005_000_000, 5_000_000)]
    [InlineData(10_000_000, 200_000_000, 12_000_000, 1_005_000_000, 5_000_000)]
    [InlineData(24_000_000, 100_000_000, 26_400_000, 1_005_000_000, 5_000_000)]
    [InlineData(10_000_000, 100_000_000, 11_000_000, 999_000_000, -1_000_000)]
    [InlineData(24_000_000, 100_000_000, 24_000_002_400_000, 1_000_000_005_000_000, 5_000_000)]
    public void PresentationTimeIsIndependentOfEventDelayAndClockFrequency(
        long frequency, long eventNanoseconds, long eventQpc, long presentationNanoseconds, long expectedNanoseconds)
    {
        var timestamp = EtwSnapTraceParser.PresentationTimestamp(
            presentationNanoseconds / 100, eventQpc, frequency, Timestamp.FromNanoseconds(eventNanoseconds));

        Assert.Equal(expectedNanoseconds, timestamp.ToNanoseconds);
    }
}