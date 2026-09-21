namespace RudolphTech.Core.Scheduling;

/// <summary>
/// The hold after a Mercado Libre verification wall: the first one that day pauses automatic runs
/// for two hours, the second for six, the third or later until the next daily survey (which is
/// never held, see <see cref="ScheduleDecider"/>). A wall on a new day starts the count over, which
/// is why the streak carries its own day rather than reusing <see cref="Hold.Until"/>'s date: a hold
/// can run past midnight, and that must not make the next day's first wall look like an escalation.
/// </summary>
public static class BlockBackoff
{
    public static readonly TimeSpan FirstHold = TimeSpan.FromHours(2);
    public static readonly TimeSpan SecondHold = TimeSpan.FromHours(6);

    /// <summary> No automatic run before <see cref="Until"/>; null means no hold at all. </summary>
    public readonly record struct Hold(DateTimeOffset? Until, int Streak, DateOnly? Day);

    /// <summary> The hold after a wall. Third or later that day: nothing automatic until the next daily survey. </summary>
    public static Hold AfterWall(DateTimeOffset now, int streak, DateOnly? streakDay, TimeOnly dailyTime)
    {
        var today = DateOnly.FromDateTime(now.Date);
        var count = streakDay == today ? streak + 1 : 1;
        DateTimeOffset until = count switch
        {
            1 => now + FirstHold,
            2 => now + SecondHold,
            _ => NextDailyAfter(now, dailyTime),
        };
        return new Hold(until, count, today);
    }

    /// <summary> The next occurrence of dailyTime strictly after now: today if still ahead of it, tomorrow once past it. </summary>
    public static DateTimeOffset NextDailyAfter(DateTimeOffset now, TimeOnly dailyTime)
    {
        var today = DateOnly.FromDateTime(now.Date);
        var scheduled = new DateTimeOffset(today.ToDateTime(dailyTime), now.Offset);
        return now < scheduled ? scheduled : new DateTimeOffset(today.AddDays(1).ToDateTime(dailyTime), now.Offset);
    }

    /// <summary> A run that finished clean: every field back to nothing. </summary>
    public static Hold Cleared() => new(null, 0, null);
}
