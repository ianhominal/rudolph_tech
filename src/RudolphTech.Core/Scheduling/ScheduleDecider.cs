using RudolphTech.Core.Settings;

namespace RudolphTech.Core.Scheduling;

/// <summary> What the timer should start right now, if anything. </summary>
public enum ScheduledAction
{
    None,

    /// <summary> meli-survey.mjs --pending: serves a request left by the web app's "Actualizar" button. </summary>
    Pending,

    /// <summary> meli-survey.mjs --all: the full daily survey. </summary>
    Daily,
}

/// <summary> Everything the decision depends on, so it can be taken without touching a clock or the disk. </summary>
public sealed class ScheduleInputs
{
    public required DateTimeOffset Now { get; init; }
    public required int PendingIntervalMinutes { get; init; }
    public required TimeOnly DailyTime { get; init; }
    public bool Paused { get; init; }
    public bool Configured { get; init; }
    public bool IsRunning { get; init; }
    public DateTimeOffset? LastPendingRun { get; init; }
    public DateOnly? LastDailyRun { get; init; }

    /// <summary> Off means the daily survey never starts on its own, whatever the clock says. </summary>
    public bool DailyEnabled { get; init; } = true;

    /// <summary> Off means the pending check never starts on its own, whatever the clock says. </summary>
    public bool PendingEnabled { get; init; } = true;
}

/// <summary>
/// The whole schedule, as one pure function. Replaces the two Windows Scheduled Tasks the old zip
/// installed: every N minutes a "--pending" check, once a day the full "--all" survey, never two at
/// the same time.
/// </summary>
public static class ScheduleDecider
{
    /// <summary>
    /// How long after its time a first ever daily survey may still start. With no history at all
    /// (the app was just installed) a run that starts many hours late would surprise whoever is
    /// using the PC, since it opens a real Chrome window; it waits for tomorrow instead. Once there
    /// is a recorded run the catch up is unlimited, so a machine that was off all morning still
    /// gets its survey when it boots.
    /// </summary>
    public static readonly TimeSpan FirstDailyGrace = TimeSpan.FromHours(3);

    public static ScheduledAction Decide(ScheduleInputs inputs)
    {
        if (inputs.IsRunning || inputs.Paused || !inputs.Configured) return ScheduledAction.None;
        if (IsDailyDue(inputs)) return ScheduledAction.Daily;
        if (IsPendingDue(inputs)) return ScheduledAction.Pending;
        return ScheduledAction.None;
    }

    private static bool IsDailyDue(ScheduleInputs inputs)
    {
        if (!inputs.DailyEnabled) return false;

        var today = DateOnly.FromDateTime(inputs.Now.Date);
        if (inputs.LastDailyRun is { } last && last >= today) return false;

        var scheduled = new DateTimeOffset(today.ToDateTime(inputs.DailyTime), inputs.Now.Offset);
        if (inputs.Now < scheduled) return false;

        return inputs.LastDailyRun is not null || inputs.Now - scheduled <= FirstDailyGrace;
    }

    private static bool IsPendingDue(ScheduleInputs inputs)
    {
        if (!inputs.PendingEnabled) return false;
        if (inputs.LastPendingRun is not { } last) return true;

        return inputs.Now - last >= TimeSpan.FromMinutes(EffectiveIntervalMinutes(inputs));
    }

    /// <summary> Hand edited or outdated values come back into range here, the same way AppSettings.Clamp does on load. </summary>
    private static int EffectiveIntervalMinutes(ScheduleInputs inputs) =>
        inputs.PendingIntervalMinutes is >= AppSettings.MinimumPendingIntervalMinutes
            and <= AppSettings.MaximumPendingIntervalMinutes
            ? inputs.PendingIntervalMinutes
            : AppSettings.DefaultPendingIntervalMinutes;

    /// <summary>
    /// When the next survey is due, or null when none is scheduled at all (paused, unlinked, or both
    /// schedules off). The same rules as <see cref="Decide"/> read forwards instead of as a yes or no:
    /// the web app shows this as "proximo relevamiento en 4 min" next to whether the program is open
    /// (web/src/lib/agent-presence.ts), so the two must never disagree about what is about to happen.
    ///
    /// Deliberately blind to <see cref="ScheduleInputs.IsRunning"/>: a survey already in progress is
    /// not a schedule, and the web says so on its own line.
    ///
    /// An overdue run reports the time it was due rather than "now", so a PC that was asleep does not
    /// show a countdown that keeps restarting.
    /// </summary>
    public static DateTimeOffset? NextRunAt(ScheduleInputs inputs)
    {
        if (inputs.Paused || !inputs.Configured) return null;

        DateTimeOffset? daily = inputs.DailyEnabled ? NextDailyAt(inputs) : null;
        DateTimeOffset? pending = inputs.PendingEnabled ? NextPendingAt(inputs) : null;

        if (daily is null) return pending;
        if (pending is null) return daily;
        return daily < pending ? daily : pending;
    }

    private static DateTimeOffset NextPendingAt(ScheduleInputs inputs) =>
        inputs.LastPendingRun is { } last
            ? last + TimeSpan.FromMinutes(EffectiveIntervalMinutes(inputs))
            : inputs.Now;

    private static DateTimeOffset NextDailyAt(ScheduleInputs inputs)
    {
        var today = DateOnly.FromDateTime(inputs.Now.Date);
        var scheduled = new DateTimeOffset(today.ToDateTime(inputs.DailyTime), inputs.Now.Offset);

        // Already done today, so the next one is tomorrow's.
        if (inputs.LastDailyRun is { } last && last >= today) return scheduled.AddDays(1);

        if (inputs.Now < scheduled) return scheduled;

        // Past its time and not run yet: due, unless this would be a first ever survey so late that
        // IsDailyDue has already given up on today (same FirstDailyGrace rule).
        return inputs.LastDailyRun is not null || inputs.Now - scheduled <= FirstDailyGrace
            ? scheduled
            : scheduled.AddDays(1);
    }
}
