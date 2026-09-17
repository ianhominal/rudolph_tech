using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Turning "the next survey is at 06:45" into what the web app accepts. This is the piece that decides
/// whether a perfectly good heartbeat comes back as a 400, so the range it clamps to has to be the same
/// one web/src/lib/agent-presence.ts validates against.
/// </summary>
public class HeartbeatTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void CountsTheSecondsUntilTheNextSurvey()
    {
        var heartbeat = Heartbeat.For(Now.AddMinutes(4), Now, paused: false, running: false, servesRequests: true);
        Assert.Equal(240, heartbeat.NextRunInSeconds);
    }

    [Fact]
    public void ReportsNothingScheduledAsNull()
    {
        Assert.Null(Heartbeat.For(null, Now, paused: true, running: false, servesRequests: true).NextRunInSeconds);
    }

    [Fact]
    public void ReportsAnOverdueSurveyAsZeroRatherThanAsANegativeTheWebWouldRefuse()
    {
        // The web app rejects anything below zero with a 400. A PC that was asleep past its turn must not
        // turn every one of its heartbeats into a rejected request.
        var heartbeat = Heartbeat.For(Now.AddMinutes(-30), Now, paused: false, running: false, servesRequests: true);
        Assert.Equal(0, heartbeat.NextRunInSeconds);
    }

    [Fact]
    public void CapsAnAbsurdlyDistantSurveyAtTheSameLimitTheWebAccepts()
    {
        var heartbeat = Heartbeat.For(Now.AddDays(400), Now, paused: false, running: false, servesRequests: true);
        Assert.Equal(31 * 24 * 60 * 60, heartbeat.NextRunInSeconds);
    }

    [Fact]
    public void RoundsToTheNearestSecond()
    {
        Assert.Equal(2, Heartbeat.For(Now.AddMilliseconds(1600), Now, paused: false, running: false, servesRequests: true).NextRunInSeconds);
    }

    [Fact]
    public void CarriesTheThreeFlagsThroughUntouched()
    {
        var heartbeat = Heartbeat.For(null, Now, paused: true, running: true, servesRequests: false);
        Assert.True(heartbeat.Paused);
        Assert.True(heartbeat.Running);
        Assert.False(heartbeat.ServesRequests);
    }
}
