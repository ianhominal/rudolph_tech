using RudolphTech.Core.Scheduling;
using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The hold after a Mercado Libre verification wall: escalates within a day, resets on a new one.
/// Pure, so the escalation and the day boundary can be pinned without touching a clock.
/// </summary>
public class BlockBackoffTests
{
    private static readonly TimeOnly DailyTime = new(6, 45);

    [Fact]
    public void TheFirstWallOfTheDayHoldsForTwoHours()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));

        var hold = BlockBackoff.AfterWall(now, streak: 0, streakDay: null, DailyTime);

        Assert.Equal(now + TimeSpan.FromHours(2), hold.Until);
        Assert.Equal(1, hold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 21), hold.Day);
    }

    [Fact]
    public void TheSecondWallTheSameDayHoldsForSixHours()
    {
        var now = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3));

        var hold = BlockBackoff.AfterWall(now, streak: 1, streakDay: new DateOnly(2026, 9, 21), DailyTime);

        Assert.Equal(now + TimeSpan.FromHours(6), hold.Until);
        Assert.Equal(2, hold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 21), hold.Day);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TheThirdOrLaterWallTheSameDayHoldsUntilTheNextDailySurvey(int previousStreak)
    {
        var now = new DateTimeOffset(2026, 9, 21, 22, 40, 0, TimeSpan.FromHours(-3));

        var hold = BlockBackoff.AfterWall(now, streak: previousStreak, streakDay: new DateOnly(2026, 9, 21), DailyTime);

        Assert.Equal(new DateTimeOffset(2026, 9, 22, 6, 45, 0, TimeSpan.FromHours(-3)), hold.Until);
        Assert.Equal(previousStreak + 1, hold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 21), hold.Day);
    }

    [Fact]
    public void AWallOnANewDayResetsTheStreakToOne()
    {
        var now = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.FromHours(-3));

        var hold = BlockBackoff.AfterWall(now, streak: 3, streakDay: new DateOnly(2026, 9, 21), DailyTime);

        Assert.Equal(now + TimeSpan.FromHours(2), hold.Until);
        Assert.Equal(1, hold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 22), hold.Day);
    }

    [Fact]
    public void LateNightThenEarlyMorningCountAsTwoDifferentDaysFirstWalls()
    {
        var lateNight = new DateTimeOffset(2026, 9, 21, 23, 50, 0, TimeSpan.FromHours(-3));
        var firstHold = BlockBackoff.AfterWall(lateNight, streak: 0, streakDay: null, DailyTime);
        Assert.Equal(1, firstHold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 21), firstHold.Day);

        var earlyMorning = new DateTimeOffset(2026, 9, 22, 0, 10, 0, TimeSpan.FromHours(-3));
        var secondHold = BlockBackoff.AfterWall(earlyMorning, firstHold.Streak, firstHold.Day, DailyTime);

        // Only twenty minutes apart, but the calendar day rolled over, so this is treated as that new
        // day's own first wall rather than an escalation of the night before's streak.
        Assert.Equal(earlyMorning + TimeSpan.FromHours(2), secondHold.Until);
        Assert.Equal(1, secondHold.Streak);
        Assert.Equal(new DateOnly(2026, 9, 22), secondHold.Day);
    }

    [Fact]
    public void NextDailyAfterReturnsTodayWhenStillAheadOfIt()
    {
        var beforeSix = new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.FromHours(-3));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 21, 6, 45, 0, TimeSpan.FromHours(-3)),
            BlockBackoff.NextDailyAfter(beforeSix, DailyTime));
    }

    [Fact]
    public void NextDailyAfterRollsOverToTomorrowOncePastIt()
    {
        var afterSix = new DateTimeOffset(2026, 9, 21, 22, 0, 0, TimeSpan.FromHours(-3));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 22, 6, 45, 0, TimeSpan.FromHours(-3)),
            BlockBackoff.NextDailyAfter(afterSix, DailyTime));
    }

    [Fact]
    public void ClearedNullsEverything()
    {
        var cleared = BlockBackoff.Cleared();

        Assert.Null(cleared.Until);
        Assert.Equal(0, cleared.Streak);
        Assert.Null(cleared.Day);
    }

    // --- Next: turns a run's outcome into a change to the hold, or not (H1, M3) -----------------

    [Fact]
    public void NextEscalatesTheHoldExactlyLikeAfterWallWhenTheStateIsFresh()
    {
        var current = new BlockBackoff.Hold(null, 0, null);
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));

        var next = BlockBackoff.Next(current, RunOutcomeKind.Blocked, stateIsFromThisRun: true, now, DailyTime);

        Assert.Equal(now + BlockBackoff.FirstHold, next.Until);
        Assert.Equal(1, next.Streak);
        Assert.Equal(DateOnly.FromDateTime(now.Date), next.Day);
    }

    [Fact]
    public void NextClearsAnActiveHoldWhenTheStateIsFreshAndFinished()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));
        var current = new BlockBackoff.Hold(now.AddHours(2), 1, DateOnly.FromDateTime(now.Date));

        var next = BlockBackoff.Next(current, RunOutcomeKind.Finished, stateIsFromThisRun: true, now, DailyTime);

        Assert.Null(next.Until);
        Assert.Equal(0, next.Streak);
        Assert.Null(next.Day);
    }

    [Theory]
    [InlineData(RunOutcomeKind.Nothing)]
    [InlineData(RunOutcomeKind.Interrupted)]
    [InlineData(RunOutcomeKind.Error)]
    public void NextLeavesTheHoldAloneWhenAFreshOutcomeProvesNothingEitherWay(RunOutcomeKind outcomeKind)
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));
        var current = new BlockBackoff.Hold(now.AddHours(2), 1, DateOnly.FromDateTime(now.Date));

        var next = BlockBackoff.Next(current, outcomeKind, stateIsFromThisRun: true, now, DailyTime);

        Assert.Equal(current, next);
    }

    [Theory]
    [InlineData(RunOutcomeKind.Blocked)]
    [InlineData(RunOutcomeKind.Finished)]
    [InlineData(RunOutcomeKind.Nothing)]
    [InlineData(RunOutcomeKind.Interrupted)]
    [InlineData(RunOutcomeKind.Error)]
    public void NextLeavesTheHoldAloneWheneverTheStateIsStaleWhateverTheOutcomeSays(RunOutcomeKind outcomeKind)
    {
        // H1: meli-survey.mjs's "--pending, nothing due" path exits 0 without writing the state file,
        // so a state left over from an earlier run can be read as any outcome kind while proving
        // nothing about this one. A stale read must be a no-op in every direction, active hold or not.
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));
        var current = new BlockBackoff.Hold(now.AddHours(2), 1, DateOnly.FromDateTime(now.Date));

        var next = BlockBackoff.Next(current, outcomeKind, stateIsFromThisRun: false, now, DailyTime);

        Assert.Equal(current, next);
    }

    [Fact]
    public void H1RegressionAStaleBlockedReadDoesNotReArmTheHoldOnEveryTick()
    {
        // The exact production scenario from 2026-09-21: a real wall set a 2 hour hold; every later
        // "nothing pending" tick re-read that same stale blocked state. Before this fix that escalated
        // the streak on every tick (2h, then 6h, then the next daily survey, forever, then a new day
        // restarted it). A stale read is now a no-op, so only a fresh wall moves the streak.
        var now = new DateTimeOffset(2026, 9, 21, 14, 25, 0, TimeSpan.FromHours(-3));
        var afterTheRealWall = new BlockBackoff.Hold(now.AddHours(2), 1, DateOnly.FromDateTime(now.Date));

        var afterEachStaleTick = afterTheRealWall;
        foreach (var tick in new[] { now.AddMinutes(15), now.AddMinutes(30), now.AddMinutes(45) })
        {
            afterEachStaleTick = BlockBackoff.Next(afterEachStaleTick, RunOutcomeKind.Blocked, stateIsFromThisRun: false, tick, DailyTime);
        }

        Assert.Equal(afterTheRealWall, afterEachStaleTick);
    }
}
