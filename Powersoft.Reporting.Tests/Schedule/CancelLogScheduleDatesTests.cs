using FluentAssertions;
using Powersoft.Reporting.Core.Helpers;
using Xunit;

namespace Powersoft.Reporting.Tests.Schedule;

public class CancelLogScheduleDatesTests
{
    [Fact]
    public void Normalize_DateOnlyMode_KeepsDateBounds()
    {
        var from = new DateTime(2026, 1, 15, 14, 30, 0);
        var to = new DateTime(2026, 3, 20, 9, 0, 0);

        var (df, dt) = CancelLogScheduleDates.Normalize(from, to, reportByDateTime: false);

        df.Should().Be(new DateTime(2026, 1, 15));
        dt.Should().Be(new DateTime(2026, 3, 20));
    }

    [Fact]
    public void Normalize_DateTimeMode_ExtendsToEndOfDay()
    {
        var from = new DateTime(2026, 6, 1, 10, 0, 0);
        var to = new DateTime(2026, 6, 3, 8, 0, 0);

        var (df, dt) = CancelLogScheduleDates.Normalize(from, to, reportByDateTime: true);

        df.Should().Be(new DateTime(2026, 6, 1));
        dt.Should().Be(new DateTime(2026, 6, 3, 23, 59, 59));
    }
}
