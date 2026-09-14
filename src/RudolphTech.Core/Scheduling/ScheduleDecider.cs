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

        var interval = inputs.PendingIntervalMinutes is >= AppSettings.MinimumPendingIntervalMinutes
            and <= AppSettings.MaximumPendingIntervalMinutes
            ? inputs.PendingIntervalMinutes
            : AppSettings.DefaultPendingIntervalMinutes;

        return inputs.Now - last >= TimeSpan.FromMinutes(interval);
    }
}
