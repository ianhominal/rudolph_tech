using RudolphTech.Core.Scheduling;
using RudolphTech.Core.Settings;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The scheduler is the piece that replaces the two Windows Scheduled Tasks the old zip installed
/// (RudolphRelevamientoPendientes every 15 minutes, RudolphRelevamientoDiario at 06:45), so its
/// decisions are pure and covered here rather than observed by waiting on a real timer.
/// </summary>
public class ScheduleDeciderTests
{
    private static readonly DateTimeOffset Monday9Am = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(-3));

    private static ScheduleInputs Inputs(
        DateTimeOffset? now = null,
        int intervalMinutes = 15,
        TimeOnly? dailyTime = null,
        bool paused = false,
        bool configured = true,
        bool isRunning = false,
        DateTimeOffset? lastPendingRun = null,
        DateOnly? lastDailyRun = null,
        bool dailyEnabled = true,
        bool pendingEnabled = true) => new()
        {
            Now = now ?? Monday9Am,
            PendingIntervalMinutes = intervalMinutes,
            DailyTime = dailyTime ?? new TimeOnly(6, 45),
            Paused = paused,
            Configured = configured,
            IsRunning = isRunning,
            LastPendingRun = lastPendingRun,
            LastDailyRun = lastDailyRun,
            DailyEnabled = dailyEnabled,
            PendingEnabled = pendingEnabled,
        };

    [Fact]
    public void RunsThePendingCheckWhenTheIntervalElapsed()
    {
        var inputs = Inputs(
            lastPendingRun: Monday9Am.AddMinutes(-15),
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.Pending, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void WaitsWhenTheIntervalHasNotElapsedYet()
    {
        var inputs = Inputs(
            lastPendingRun: Monday9Am.AddMinutes(-14),
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void RunsThePendingCheckImmediatelyWhenItNeverRan()
    {
        var inputs = Inputs(lastPendingRun: null, lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.Pending, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void RunsTheDailySurveyOnceTheScheduledTimePassed()
    {
        var inputs = Inputs(lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.Daily, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void DoesNotRepeatTheDailySurveyOnTheSameDay()
    {
        var inputs = Inputs(
            lastPendingRun: Monday9Am,
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void WaitsForTheScheduledTimeBeforeTheDailySurvey()
    {
        var beforeSix = new DateTimeOffset(2026, 9, 14, 5, 30, 0, TimeSpan.FromHours(-3));
        var inputs = Inputs(
            now: beforeSix,
            lastPendingRun: beforeSix,
            lastDailyRun: DateOnly.FromDateTime(beforeSix.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void CatchesUpTheDailySurveyWhenThePcWasOffAllMorning()
    {
        var afternoon = new DateTimeOffset(2026, 9, 14, 16, 0, 0, TimeSpan.FromHours(-3));
        var inputs = Inputs(
            now: afternoon,
            lastPendingRun: afternoon,
            lastDailyRun: DateOnly.FromDateTime(afternoon.Date.AddDays(-3)));

        Assert.Equal(ScheduledAction.Daily, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void DoesNotStartAFirstDailySurveyLongAfterItsTime()
    {
        // No history at all (freshly installed at night): running the full survey right away would
        // surprise whoever is using the PC, so it waits for tomorrow's scheduled time.
        var night = new DateTimeOffset(2026, 9, 14, 23, 30, 0, TimeSpan.FromHours(-3));
        var inputs = Inputs(now: night, lastPendingRun: night, lastDailyRun: null);

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void StartsAFirstDailySurveyShortlyAfterItsTime()
    {
        var justAfter = new DateTimeOffset(2026, 9, 14, 7, 30, 0, TimeSpan.FromHours(-3));
        var inputs = Inputs(now: justAfter, lastPendingRun: justAfter, lastDailyRun: null);

        Assert.Equal(ScheduledAction.Daily, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void TheDailySurveyWinsOverThePendingCheck()
    {
        var inputs = Inputs(lastPendingRun: null, lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.Daily, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void NeverStartsASecondRunWhileOneIsRunning()
    {
        var inputs = Inputs(isRunning: true, lastPendingRun: null, lastDailyRun: null);

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void DoesNothingWhilePaused()
    {
        var inputs = Inputs(paused: true, lastPendingRun: null, lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void DoesNothingBeforeTheAppIsConfigured()
    {
        var inputs = Inputs(configured: false, lastPendingRun: null, lastDailyRun: null);

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AnInvalidIntervalFallsBackToTheDefault(int interval)
    {
        var inputs = Inputs(
            intervalMinutes: interval,
            lastPendingRun: Monday9Am.AddMinutes(-AppSettings.DefaultPendingIntervalMinutes + 1),
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void NeverRunsTheDailySurveyWhenItIsSwitchedOffEvenIfItsTimePassed()
    {
        var inputs = Inputs(
            dailyEnabled: false,
            lastPendingRun: Monday9Am,
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void NeverPollsForPendingRequestsWhenItIsSwitchedOff()
    {
        var inputs = Inputs(
            pendingEnabled: false,
            lastPendingRun: null,
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void StaysIdleWhenBothTheDailySurveyAndThePendingCheckAreSwitchedOff()
    {
        var inputs = Inputs(
            dailyEnabled: false,
            pendingEnabled: false,
            lastPendingRun: null,
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.None, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void StillRunsTheDailySurveyWhenOnlyPendingIsSwitchedOff()
    {
        var inputs = Inputs(
            pendingEnabled: false,
            lastPendingRun: null,
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date.AddDays(-1)));

        Assert.Equal(ScheduledAction.Daily, ScheduleDecider.Decide(inputs));
    }

    [Fact]
    public void StillPollsForPendingRequestsWhenOnlyDailyIsSwitchedOff()
    {
        var inputs = Inputs(
            dailyEnabled: false,
            lastPendingRun: Monday9Am.AddMinutes(-15),
            lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));

        Assert.Equal(ScheduledAction.Pending, ScheduleDecider.Decide(inputs));
    }
}
