using RudolphTech.Core.Scheduling;

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
}
