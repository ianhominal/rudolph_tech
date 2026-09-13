using System.Text.Json;
using System.Text.Json.Serialization;
using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Settings;

/// <summary>
/// Reads and writes settings.json. Nothing here ever throws at the caller: a missing, truncated or
/// hand edited file falls back to the defaults, because the tray app has no good way to ask a
/// question at startup.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ISecretProtector _protector;

    public SettingsStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    /// <summary> The on disk shape. Kept apart from <see cref="AppSettings"/> so the token is protected in transit. </summary>
    private sealed class Persisted
    {
        public string? AppUrl { get; set; }
        public int? PendingIntervalMinutes { get; set; }
        public string? DailyTime { get; set; }
        public bool ChromeOffScreen { get; set; }
        public bool StartWithWindows { get; set; }
        public bool Paused { get; set; }
        public string? ProtectedIngestToken { get; set; }
        public string? PackageAppUrl { get; set; }
        public DateTimeOffset? LastPackageDownload { get; set; }
        public DateTimeOffset? LastPendingRun { get; set; }
        public string? LastDailyRun { get; set; }
        public RunSummary? LastRun { get; set; }
    }

    public AppSettings Load()
    {
        var settings = new AppSettings();
        Persisted? persisted = null;
        try
        {
            if (File.Exists(_path)) persisted = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(_path), Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            persisted = null;
        }

        if (persisted is null)
        {
            settings.Clamp();
            return settings;
        }

        settings.AppUrl = string.IsNullOrWhiteSpace(persisted.AppUrl) ? AppSettings.DefaultAppUrl : persisted.AppUrl;
        settings.PendingIntervalMinutes = persisted.PendingIntervalMinutes ?? AppSettings.DefaultPendingIntervalMinutes;
        settings.DailyTime = TimeOnly.TryParse(persisted.DailyTime, out var daily) ? daily : AppSettings.DefaultDailyTime;
        settings.ChromeOffScreen = persisted.ChromeOffScreen;
        settings.StartWithWindows = persisted.StartWithWindows;
        settings.Paused = persisted.Paused;
        settings.PackageAppUrl = persisted.PackageAppUrl;
        settings.LastPackageDownload = persisted.LastPackageDownload;
        settings.LastPendingRun = persisted.LastPendingRun;
        settings.LastDailyRun = DateOnly.TryParse(persisted.LastDailyRun, out var day) ? day : null;
        settings.LastRun = persisted.LastRun;
        settings.IngestToken = string.IsNullOrEmpty(persisted.ProtectedIngestToken)
            ? null
            : _protector.Unprotect(persisted.ProtectedIngestToken);

        settings.Clamp();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var persisted = new Persisted
        {
            AppUrl = settings.AppUrl,
            PendingIntervalMinutes = settings.PendingIntervalMinutes,
            DailyTime = settings.DailyTime.ToString("HH:mm"),
            ChromeOffScreen = settings.ChromeOffScreen,
            StartWithWindows = settings.StartWithWindows,
            Paused = settings.Paused,
            ProtectedIngestToken = string.IsNullOrEmpty(settings.IngestToken) ? null : _protector.Protect(settings.IngestToken),
            PackageAppUrl = settings.PackageAppUrl,
            LastPackageDownload = settings.LastPackageDownload,
            LastPendingRun = settings.LastPendingRun,
            LastDailyRun = settings.LastDailyRun?.ToString("yyyy-MM-dd"),
            LastRun = settings.LastRun,
        };

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // Write beside the real file and move over it, so a crash halfway through never leaves an
        // unreadable settings.json behind.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(persisted, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
