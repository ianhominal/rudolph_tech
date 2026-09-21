namespace RudolphTech.Core.Settings;

/// <summary>
/// Everything the tray app remembers between runs. Persisted by <see cref="SettingsStore"/> as
/// settings.json under %LocalAppData%\RudolphTech; the ingest token is the only secret here and it
/// is written protected (DPAPI, current user), never in plain text. The web password is never
/// stored at all: it is typed when the person opens the settings window and forgotten right after.
/// </summary>
public sealed class AppSettings
{
    public const string DefaultAppUrl = "https://rudolph-mvp.vercel.app";
    /// <summary>
    /// How long a survey asked for from the web waits before the office PC even looks for it. It used
    /// to be 15, which is a long time to stare at "Relevamiento solicitado" wondering whether anything
    /// is going to happen. The floor is 5 (see <see cref="MinimumPendingIntervalMinutes"/>) because each
    /// check spawns a Node process, so there is no point asking every minute.
    /// </summary>
    public const int DefaultPendingIntervalMinutes = 5;
    public const int MinimumPendingIntervalMinutes = 5;
    public const int MaximumPendingIntervalMinutes = 720;

    /// <summary> Default 06:45, the same time the old RudolphRelevamientoDiario scheduled task used. </summary>
    public static readonly TimeOnly DefaultDailyTime = new(6, 45);

    /// <summary> Base address of the web app, with no trailing slash (see <see cref="Web.AppUrl"/>). </summary>
    public string AppUrl { get; set; } = DefaultAppUrl;

    /// <summary> How often the "--pending" check runs. The script exits in a second when nothing is pending. </summary>
    public int PendingIntervalMinutes { get; set; } = DefaultPendingIntervalMinutes;

    /// <summary> Time of day for the full "--all" survey. </summary>
    public TimeOnly DailyTime { get; set; } = DefaultDailyTime;

    /// <summary> Launches the survey's Chrome window off screen (SURVEY_CHROME_MINIMIZED=1). Never headless. </summary>
    public bool ChromeOffScreen { get; set; }

    /// <summary> Registers the app under the current user's Run key. </summary>
    public bool StartWithWindows { get; set; }

    /// <summary> While paused no scheduled run starts. "Relevar ahora" still works. </summary>
    public bool Paused { get; set; }

    /// <summary> Whether the daily full survey is scheduled at all. Off means it never runs on its own. </summary>
    public bool DailyEnabled { get; set; } = true;

    /// <summary> Whether the periodic check for pending requests from the web app is scheduled at all. </summary>
    public bool PendingEnabled { get; set; } = true;

    /// <summary> SURVEY_INGEST_TOKEN, read out of the downloaded package's .env. Stored protected. </summary>
    public string? IngestToken { get; set; }

    /// <summary> The RUDOLPH_APP_URL the downloaded package itself carried, for the settings window to show. </summary>
    public string? PackageAppUrl { get; set; }

    public DateTimeOffset? LastPackageDownload { get; set; }

    public DateTimeOffset? LastPendingRun { get; set; }

    /// <summary> Day of the last full survey, so a machine that was off at 06:45 catches up when it boots. </summary>
    public DateOnly? LastDailyRun { get; set; }

    public Survey.RunSummary? LastRun { get; set; }

    /// <summary> No automatic pending check starts before this. The daily survey is never held. Null means no hold. </summary>
    public DateTimeOffset? BlockedUntil { get; set; }

    /// <summary> How many verification walls happened on <see cref="BlockedStreakDay"/>. Decides how long the next hold is. </summary>
    public int BlockedStreak { get; set; }

    /// <summary> The day <see cref="BlockedStreak"/> counts for. A wall on a new day starts the count over. </summary>
    public DateOnly? BlockedStreakDay { get; set; }

    /// <summary> Nothing can run before the first login handed us a url and a token. </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppUrl) && !string.IsNullOrWhiteSpace(IngestToken);

    /// <summary> Brings hand edited or outdated values back into range; called on every load. </summary>
    public void Clamp()
    {
        if (string.IsNullOrWhiteSpace(AppUrl)) AppUrl = DefaultAppUrl;
        if (PendingIntervalMinutes < MinimumPendingIntervalMinutes || PendingIntervalMinutes > MaximumPendingIntervalMinutes)
        {
            PendingIntervalMinutes = DefaultPendingIntervalMinutes;
        }
    }
}
