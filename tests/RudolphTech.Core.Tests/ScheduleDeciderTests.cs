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

    // --- NextRunAt: the same rules, read forwards ---------------------------
    //
    // The web app shows "proximo relevamiento en 4 min" from this, so it must agree with Decide:
    // whatever instant this returns is the first one at which Decide stops answering None.

    private static readonly DateTimeOffset Monday5Am = new(2026, 9, 14, 5, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void NextRunAtIsNothingWhilePaused()
    {
        Assert.Null(ScheduleDecider.NextRunAt(Inputs(paused: true)));
    }

    [Fact]
    public void NextRunAtIsNothingBeforeThePcIsLinked()
    {
        Assert.Null(ScheduleDecider.NextRunAt(Inputs(configured: false)));
    }

    [Fact]
    public void NextRunAtIsNothingWhenNeitherScheduleIsEnabled()
    {
        Assert.Null(ScheduleDecider.NextRunAt(Inputs(dailyEnabled: false, pendingEnabled: false)));
    }

    [Fact]
    public void NextPendingCheckIsDueRightAwayWhenThereIsNoHistory()
    {
        var inputs = Inputs(dailyEnabled: false);
        Assert.Equal(Monday9Am, ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void NextPendingCheckIsOneIntervalAfterTheLastOne()
    {
        var inputs = Inputs(intervalMinutes: 5, lastPendingRun: Monday9Am.AddMinutes(-2), dailyEnabled: false);
        Assert.Equal(Monday9Am.AddMinutes(3), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void AnOverduePendingCheckReportsItsOwnDueTimeRatherThanNow()
    {
        // Already late (the PC was asleep): the honest answer is the time it was due, which reads as
        // "arranca en un momento" on the web rather than as a countdown that never reaches zero.
        var inputs = Inputs(intervalMinutes: 5, lastPendingRun: Monday9Am.AddMinutes(-30), dailyEnabled: false);
        Assert.Equal(Monday9Am.AddMinutes(-25), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void AnIntervalOutOfRangeFallsBackToTheDefault()
    {
        var inputs = Inputs(intervalMinutes: 0, lastPendingRun: Monday9Am, dailyEnabled: false);
        Assert.Equal(Monday9Am.AddMinutes(AppSettings.DefaultPendingIntervalMinutes), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void TheDailySurveyWinsWhenItComesFirst()
    {
        // 05:00, daily at 06:45, and the pending check is three hours away.
        var inputs = Inputs(
            now: Monday5Am,
            intervalMinutes: 180,
            lastPendingRun: Monday5Am,
            lastDailyRun: DateOnly.FromDateTime(Monday5Am.Date).AddDays(-1));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 6, 45, 0, TimeSpan.FromHours(-3)), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void ThePendingCheckWinsWhenItComesFirst()
    {
        var inputs = Inputs(
            now: Monday5Am,
            intervalMinutes: 5,
            lastPendingRun: Monday5Am,
            lastDailyRun: DateOnly.FromDateTime(Monday5Am.Date).AddDays(-1));
        Assert.Equal(Monday5Am.AddMinutes(5), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void TheDailySurveyRollsOverToTomorrowOnceItHasRunToday()
    {
        var inputs = Inputs(lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date), pendingEnabled: false);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 6, 45, 0, TimeSpan.FromHours(-3)), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void AFirstEverDailySurveyPastItsGraceWaitsForTomorrow()
    {
        // Same rule as Decide: with no history at all, a survey many hours late would surprise whoever
        // is at the PC, since it opens a real Chrome window. 10:00 is past 06:45 plus the three hour grace.
        var inputs = Inputs(now: new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(-3)), lastDailyRun: null, pendingEnabled: false);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 6, 45, 0, TimeSpan.FromHours(-3)), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void ADailySurveyStillInsideItsGraceIsDueAtItsOwnTime()
    {
        var inputs = Inputs(now: new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.FromHours(-3)), lastDailyRun: null, pendingEnabled: false);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 6, 45, 0, TimeSpan.FromHours(-3)), ScheduleDecider.NextRunAt(inputs));
    }

    [Fact]
    public void NextRunAtAgreesWithDecideAboutWhatIsDueNow()
    {
        // The contract between the two: if Decide wants to start something, NextRunAt is not in the future.
        var inputs = Inputs(intervalMinutes: 5, lastPendingRun: Monday9Am.AddMinutes(-5), lastDailyRun: DateOnly.FromDateTime(Monday9Am.Date));
        Assert.NotEqual(ScheduledAction.None, ScheduleDecider.Decide(inputs));
        Assert.True(ScheduleDecider.NextRunAt(inputs) <= inputs.Now);
    }
}
