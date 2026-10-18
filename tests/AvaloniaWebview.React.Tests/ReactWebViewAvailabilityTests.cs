namespace AvaloniaWebview.React.Tests;

public sealed class ReactWebViewAvailabilityTests
{
    [Fact]
    public void Check_returns_a_consistent_current_platform_report()
    {
        var report = ReactWebViewAvailability.Check();

        Assert.NotEqual(ReactWebViewPlatform.Unknown, report.Platform);
        Assert.False(string.IsNullOrWhiteSpace(report.Message));
        Assert.NotEmpty(report.Details);
        Assert.Equal(report.Status == ReactWebViewAvailabilityStatus.Available, report.IsAvailable);
        if (report.IsAvailable)
        {
            Assert.Null(report.Guidance);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(report.Guidance));
        }

        Assert.Contains(report.Status.ToString(), report.ToString());
    }

    [Fact]
    public void EnsureAvailable_matches_the_check_result()
    {
        var report = ReactWebViewAvailability.Check();

        if (report.IsAvailable)
        {
            var ensured = ReactWebViewAvailability.EnsureAvailable();
            Assert.True(ensured.IsAvailable);
            Assert.Equal(report.Platform, ensured.Platform);
        }
        else
        {
            var exception = Assert.Throws<PlatformNotSupportedException>(
                () => ReactWebViewAvailability.EnsureAvailable());
            Assert.Contains(report.Message, exception.Message);
        }
    }
}
