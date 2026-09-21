using RudolphTech.Core.Survey;

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

    /// <summary>
    /// What the hold becomes after one run, given only what a caller with no state of its own needs:
    /// the current hold, what the run's outcome was, and whether that outcome actually came from a
    /// state file this run wrote. A stale state proves nothing about what just happened (H1: the
    /// script can exit 0 without touching the state file at all, on the "--pending, nothing due" path),
    /// so it neither escalates nor clears; <paramref name="current"/> comes back unchanged.
    /// </summary>
    public static Hold Next(Hold current, RunOutcomeKind outcomeKind, bool stateIsFromThisRun, DateTimeOffset now, TimeOnly dailyTime)
    {
        if (!stateIsFromThisRun) return current;

        return outcomeKind switch
        {
            RunOutcomeKind.Blocked => AfterWall(now, current.Streak, current.Day, dailyTime),
            // Only a run that actually finished proves the block is gone. Nothing (no work found)
            // proves nothing either way, and neither does Interrupted or Error, so they leave the
            // hold exactly as it was.
            RunOutcomeKind.Finished => Cleared(),
            _ => current,
        };
    }

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
