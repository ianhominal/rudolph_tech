using RudolphTech.Core.Agent;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The package is downloaded again once a day, right before the daily survey, so the scripts on the
/// office PC never fall behind the deployed web app.
/// </summary>
public class PackageRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 6, 45, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void DownloadsWhenNothingWasEverDownloaded()
    {
        Assert.True(PackageRefreshPolicy.NeedsRefresh(null, Now, agentInstalled: false));
    }

    [Fact]
    public void DownloadsWhenTheLastDownloadWasOnAnEarlierDay()
    {
        Assert.True(PackageRefreshPolicy.NeedsRefresh(Now.AddDays(-1), Now, agentInstalled: true));
    }

    [Fact]
    public void DoesNotDownloadTwiceOnTheSameDay()
    {
        Assert.False(PackageRefreshPolicy.NeedsRefresh(Now.AddHours(-2), Now, agentInstalled: true));
    }

    [Fact]
    public void DownloadsAgainWhenTheScriptsAreMissingEvenIfTheDateSaysOtherwise()
    {
        Assert.True(PackageRefreshPolicy.NeedsRefresh(Now.AddMinutes(-5), Now, agentInstalled: false));
    }

    [Fact]
    public void AFutureDateStillCountsAsUpToDate()
    {
        // Someone moved the machine clock back; do not download in a loop because of it.
        Assert.False(PackageRefreshPolicy.NeedsRefresh(Now.AddDays(1), Now, agentInstalled: true));
    }
}
