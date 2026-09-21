using RudolphTech.Core.Settings;

namespace RudolphTech.Core.Tests;

/// <summary> settings.json under %LocalAppData%\RudolphTech, with the ingest token protected at rest. </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rudolph-tech-tests", Guid.NewGuid().ToString("n"));
    private readonly FakeSecretProtector _protector = new();

    private SettingsStore NewStore() => new(Path.Combine(_folder, "settings.json"), _protector);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary> Stands in for DPAPI in the tests: reversible, and never the plain text itself. </summary>
    private sealed class FakeSecretProtector : ISecretProtector
    {
        private const string Prefix = "protegido:";

        public string Protect(string value) => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

        public string? Unprotect(string value)
        {
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value[Prefix.Length..]));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    [Fact]
    public void TheDefaultsAreTheOnesAFreshInstallRunsWith()
    {
        var settings = NewStore().Load();

        Assert.Equal("https://rudolph-mvp.vercel.app", settings.AppUrl);
        // Five, not the old scheduled task's fifteen: that is how long a survey asked for from the web
        // sits there before this PC even looks for it, and fifteen minutes of "Relevamiento solicitado"
        // is indistinguishable from nothing happening at all.
        Assert.Equal(5, settings.PendingIntervalMinutes);
        Assert.Equal(new TimeOnly(6, 45), settings.DailyTime);
        Assert.False(settings.Paused);
        Assert.False(settings.ChromeOffScreen);
        Assert.True(settings.DailyEnabled);
        Assert.True(settings.PendingEnabled);
        Assert.Null(settings.IngestToken);
    }

    [Fact]
    public void SavesAndReadsBackEveryField()
    {
        var store = NewStore();
        var settings = store.Load();
        settings.AppUrl = "https://otra.test";
        settings.PendingIntervalMinutes = 30;
        settings.DailyTime = new TimeOnly(7, 15);
        settings.ChromeOffScreen = true;
        settings.StartWithWindows = true;
        settings.Paused = true;
        settings.DailyEnabled = false;
        settings.PendingEnabled = false;
        settings.IngestToken = "un-token";
        settings.LastPackageDownload = new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.Zero);
        settings.LastDailyRun = new DateOnly(2026, 9, 13);
        settings.LastPendingRun = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);
        store.Save(settings);

        var reloaded = NewStore().Load();

        Assert.Equal("https://otra.test", reloaded.AppUrl);
        Assert.Equal(30, reloaded.PendingIntervalMinutes);
        Assert.Equal(new TimeOnly(7, 15), reloaded.DailyTime);
        Assert.True(reloaded.ChromeOffScreen);
        Assert.True(reloaded.StartWithWindows);
        Assert.True(reloaded.Paused);
        Assert.False(reloaded.DailyEnabled);
        Assert.False(reloaded.PendingEnabled);
        Assert.Equal("un-token", reloaded.IngestToken);
        Assert.Equal(new DateOnly(2026, 9, 13), reloaded.LastDailyRun);
        Assert.NotNull(reloaded.LastPackageDownload);
        Assert.NotNull(reloaded.LastPendingRun);
    }

    [Fact]
    public void TheTokenIsNeverWrittenInPlainText()
    {
        var store = NewStore();
        var settings = store.Load();
        settings.IngestToken = "muy-secreto";
        store.Save(settings);

        var raw = File.ReadAllText(Path.Combine(_folder, "settings.json"));

        Assert.DoesNotContain("muy-secreto", raw);
        Assert.Contains("protegido:", raw);
    }

    [Fact]
    public void ATokenThatCannotBeUnprotectedIsDropped()
    {
        // What happens when settings.json is copied to another Windows user or another machine.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "settings.json"),
            """{ "appUrl": "https://x.test", "protectedIngestToken": "basura-de-otra-maquina" }""");

        var settings = NewStore().Load();

        Assert.Null(settings.IngestToken);
        Assert.Equal("https://x.test", settings.AppUrl);
    }

    [Fact]
    public void ABrokenFileFallsBackToTheDefaultsInsteadOfCrashing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ esto no es json");

        var settings = NewStore().Load();

        Assert.Equal("https://rudolph-mvp.vercel.app", settings.AppUrl);
    }

    [Fact]
    public void OutOfRangeValuesAreBroughtBackIntoRange()
    {
        var store = NewStore();
        var settings = store.Load();
        settings.PendingIntervalMinutes = 0;
        store.Save(settings);

        Assert.Equal(AppSettings.DefaultPendingIntervalMinutes, NewStore().Load().PendingIntervalMinutes);
    }

    [Fact]
    public void AnOlderSettingsFileWithoutTheNewSwitchesDefaultsBothToEnabled()
    {
        // settings.json written before DailyEnabled and PendingEnabled existed: they must not turn
        // into an accidentally paused schedule for whoever upgrades.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "settings.json"),
            """{ "appUrl": "https://x.test" }""");

        var settings = NewStore().Load();

        Assert.True(settings.DailyEnabled);
        Assert.True(settings.PendingEnabled);
    }

    [Fact]
    public void TheAppIsOnlyConfiguredOnceItHasAUrlAndAToken()
    {
        var settings = new AppSettings();
        Assert.False(settings.IsConfigured);

        settings.IngestToken = "t";
        Assert.True(settings.IsConfigured);

        settings.AppUrl = "   ";
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void TheBackoffFieldsRoundTripThroughSaveAndLoad()
    {
        var store = NewStore();
        var settings = store.Load();
        settings.BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 35, 1, TimeSpan.FromHours(-3));
        settings.BlockedStreak = 1;
        settings.BlockedStreakDay = new DateOnly(2026, 9, 21);
        store.Save(settings);

        var reloaded = NewStore().Load();

        Assert.Equal(new DateTimeOffset(2026, 9, 21, 16, 35, 1, TimeSpan.FromHours(-3)), reloaded.BlockedUntil);
        Assert.Equal(1, reloaded.BlockedStreak);
        Assert.Equal(new DateOnly(2026, 9, 21), reloaded.BlockedStreakDay);
    }

    [Fact]
    public void AnExplicitNullBlockedStreakDoesNotDiscardTheWholeFile()
    {
        // A hand edited settings.json with "blockedStreak": null must not throw the whole persisted
        // object away: every other field, in particular the ingest token that links this PC to the
        // web app, has to survive it.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "settings.json"),
            """{ "appUrl": "https://x.test", "protectedIngestToken": "protegido:dGVzdA==", "blockedStreak": null }""");

        var settings = NewStore().Load();

        Assert.Equal("https://x.test", settings.AppUrl);
        Assert.Equal("test", settings.IngestToken);
        Assert.Equal(0, settings.BlockedStreak);
    }

    [Fact]
    public void AbsentBackoffKeysDefaultToNoHold()
    {
        // settings.json written before the backoff fields existed: a run must not appear blocked with no cause.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "settings.json"),
            """{ "appUrl": "https://x.test" }""");

        var settings = NewStore().Load();

        Assert.Null(settings.BlockedUntil);
        Assert.Equal(0, settings.BlockedStreak);
        Assert.Null(settings.BlockedStreakDay);
    }
}
