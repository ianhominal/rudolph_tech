using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary> Every line the settings window and the tray show, in Spanish and without dashes. </summary>
public class StatusTextTests
{
    [Fact]
    public void SaysWhenThePackageWasLastDownloaded()
    {
        var text = StatusText.PackageStatus(new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.FromHours(-3)), agentInstalled: true);

        Assert.Contains("13/09/2026", text);
        Assert.Contains("06:45", text);
    }

    [Fact]
    public void SaysWhenThePackageWasNeverDownloaded()
    {
        var text = StatusText.PackageStatus(null, agentInstalled: false);

        Assert.Contains("todavía", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummarisesTheLastRun()
    {
        var summary = new RunSummary
        {
            Kind = SurveyRunKind.Daily,
            StartedAt = new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.FromHours(-3)),
            FinishedAt = new DateTimeOffset(2026, 9, 13, 7, 2, 0, TimeSpan.FromHours(-3)),
            Outcome = RunOutcomeKind.Finished,
            Products = 3,
            Listings = 18,
            Message = "Relevamiento terminado: 18 publicaciones de 3 productos.",
        };

        var text = StatusText.LastRun(summary);

        Assert.Contains("06:45", text);
        Assert.Contains("07:02", text);
        Assert.Contains("3", text);
        Assert.Contains("18", text);
        Assert.DoesNotContain('—', text);
    }

    [Fact]
    public void SaysWhenThereWasNoRunYet()
    {
        Assert.Contains("todavía", StatusText.LastRun(null), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummarisesTheLinkWithThePackageDate()
    {
        var text = StatusText.LinkSummary(
            "https://rudolph-mvp.vercel.app",
            new DateTimeOffset(2026, 9, 13, 17, 55, 0, TimeSpan.FromHours(-3)));

        Assert.Contains("https://rudolph-mvp.vercel.app", text);
        Assert.Contains("13/09/2026", text);
        Assert.Contains("17:55", text);
    }

    [Fact]
    public void SaysTheAgentWasNeverDownloadedInTheLinkSummary()
    {
        var text = StatusText.LinkSummary("https://rudolph-mvp.vercel.app", null);

        Assert.Contains("todavía", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribesTheSchedule()
    {
        var settings = new AppSettings { PendingIntervalMinutes = 15, DailyTime = new TimeOnly(6, 45) };

        var text = StatusText.Schedule(settings, paused: false, DateTimeOffset.Now, resumesAt: null);

        Assert.Contains("15", text);
        Assert.Contains("06:45", text);
    }

    [Fact]
    public void SaysThatEverythingIsPaused()
    {
        var settings = new AppSettings();

        Assert.Contains("pausa", StatusText.Schedule(settings, paused: true, DateTimeOffset.Now, resumesAt: null), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScheduleBalloonDoesNotPromiseAFiveMinuteCheckDuringAHold()
    {
        // Found in the independent review of A1: after a wall, this balloon (shown when Reanudar is
        // pressed) kept saying "Atiende pedidos cada 5 minutos" even though BlockBackoff was holding
        // every automatic pending check off for hours. A claim the app cannot back, same standing rule
        // as the tooltip.
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var until = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { PendingIntervalMinutes = 5, BlockedUntil = until };

        var text = StatusText.Schedule(settings, paused: false, now, resumesAt: until);

        Assert.DoesNotContain("cada", text);
        Assert.Contains("se reanudan a las 16:40", text);
    }

    [Fact]
    public void TheScheduleBalloonIgnoresAHoldThatAlreadyExpired()
    {
        var now = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings
        {
            PendingIntervalMinutes = 5,
            DailyTime = new TimeOnly(6, 45),
            BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3)),
        };

        var text = StatusText.Schedule(settings, paused: false, now, resumesAt: settings.BlockedUntil);

        Assert.Contains("cada", text);
    }

    [Fact]
    public void TheScheduleBalloonNamesTheDailyRunWhenItFallsInsideTheHoldWindow()
    {
        // H-1: BB-4 makes the daily survey immune to a hold, so when the raw hold outlasts the next
        // daily run, the real next automatic event is the (sooner) daily one. The caller is the one
        // that knows this (via AgentService.NextRunAt, which reads ScheduleDecider.NextRunAt); this
        // pins that Schedule names whatever resumesAt says, not the raw settings.BlockedUntil, once a
        // hold is in effect.
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { PendingIntervalMinutes = 5, BlockedUntil = now.AddHours(3) };
        var resumesAt = now.AddHours(1);

        var text = StatusText.Schedule(settings, paused: false, now, resumesAt);

        Assert.Contains($"se reanudan a las {resumesAt:HH:mm}", text);
        Assert.DoesNotContain("17:00", text);
    }

    [Fact]
    public void TheTrayTooltipFitsInWindowsSixtyThreeCharacterLimit()
    {
        var settings = new AppSettings { PendingIntervalMinutes = 120, DailyTime = new TimeOnly(23, 59) };
        var now = DateTimeOffset.Now;

        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: true, now, resumesAt: null).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: true, running: false, now, resumesAt: null).Length, 1, 63);
    }

    [Fact]
    public void TheTooltipDuringAWaitAsksToVerifyInChrome()
    {
        // TO-2/BB-8's text (T1): a live sub-state of "running", so it has to win over the plain
        // "relevando ahora" the same running flag would otherwise show.
        var settings = new AppSettings();
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));

        var text = StatusText.TrayTooltip(settings, paused: false, running: true, now, resumesAt: null, waiting: true);

        Assert.Equal("Rudolph Tech: hay que verificar en la ventana de Chrome", text);
    }

    [Fact]
    public void TheTooltipDuringAHoldThatEndsTodaySaysTheTime()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3)) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now, resumesAt: settings.BlockedUntil);

        Assert.Equal("Rudolph Tech: sin relevar hasta las 16:40", text);
    }

    [Fact]
    public void TheTooltipDuringAHoldThatEndsTomorrowSaysManana()
    {
        var now = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = new DateTimeOffset(2026, 9, 22, 6, 45, 0, TimeSpan.FromHours(-3)) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now, resumesAt: settings.BlockedUntil);

        Assert.Equal("Rudolph Tech: sin relevar hasta mañana 06:45", text);
    }

    [Fact]
    public void TheTooltipNamesTheDailyRunWhenItFallsInsideTheHoldWindow()
    {
        // H-1 (blocking, second independent review): reproduced with DailyEnabled true, DailyTime
        // 06:45 and a wall that persisted a 07:00 hold, this branch used to say "sin relevar hasta las
        // 07:00" while the daily survey (immune to any hold, BB-4) really ran at 06:45, opening Chrome
        // on the office PC after the tray had just told the person it would not. resumesAt is the one
        // number every surface must agree on (see StatusText.TrayTooltip's own doc comment).
        var now = new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = new DateTimeOffset(2026, 9, 21, 7, 0, 0, TimeSpan.FromHours(-3)) };
        var resumesAt = new DateTimeOffset(2026, 9, 21, 6, 45, 0, TimeSpan.FromHours(-3));

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now, resumesAt);

        Assert.Equal("Rudolph Tech: sin relevar hasta las 06:45", text);
        Assert.DoesNotContain("07:00", text);
    }

    [Fact]
    public void TheTooltipNeverNamesARawHoldBeyondWhatResumesAtSays()
    {
        // M-2: a settings.BlockedUntil far beyond ScheduleDecider.MaximumHoldHorizon (a stepped-back
        // clock, a settings.json copied from another machine) used to make this branch claim a bogus
        // far date while Decide() ignored that same value entirely and kept running every few minutes.
        // Moot once the caller always supplies resumesAt from NextRunAt, whose own pending leg already
        // discards a BlockedUntil past the horizon (pinned separately in
        // ScheduleDeciderTests.NextRunAtIgnoresABlockedUntilFartherOutThanTheMaximumHoldHorizon); this
        // pins that the tooltip itself only ever trusts what it is given, never the raw settings value.
        var now = new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = now.AddDays(5) };
        var resumesAt = now.AddMinutes(5);

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now, resumesAt);

        Assert.Equal($"Rudolph Tech: sin relevar hasta las {resumesAt:HH:mm}", text);
        Assert.DoesNotContain("/", text);
    }

    [Fact]
    public void PauseWinsOverAHeldTooltip()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = now.AddHours(2) };

        var text = StatusText.TrayTooltip(settings, paused: true, running: false, now, resumesAt: settings.BlockedUntil);

        Assert.Equal("Rudolph Tech: en pausa", text);
    }

    [Fact]
    public void TheTooltipWhileRunningIgnoresAnyHold()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = now.AddHours(2) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: true, now, resumesAt: settings.BlockedUntil);

        Assert.Equal("Rudolph Tech: relevando ahora", text);
    }

    [Fact]
    public void AnExpiredHoldFallsBackToTheNormalTooltip()
    {
        var now = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings
        {
            PendingIntervalMinutes = 5,
            DailyTime = new TimeOnly(6, 45),
            BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3)),
        };

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now, resumesAt: settings.BlockedUntil);

        Assert.Contains("diario", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTrayTooltipBranchFitsInSixtyThreeCharactersEvenWithAFarOffHold(bool waiting)
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { PendingIntervalMinutes = 120, DailyTime = new TimeOnly(23, 59), BlockedUntil = now.AddDays(3) };

        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: true, now, settings.BlockedUntil, waiting).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: true, running: false, now, settings.BlockedUntil, waiting).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: false, now, settings.BlockedUntil, waiting).Length, 1, 63);
    }

    [Fact]
    public void ResumeAtNamesTheTimeAloneForToday()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var until = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3));

        Assert.Equal("a las 16:40", StatusText.ResumeAt(until, now));
    }

    [Fact]
    public void ResumeAtNamesTomorrowAcrossMidnight()
    {
        var now = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.FromHours(-3));
        var until = new DateTimeOffset(2026, 9, 22, 6, 45, 0, TimeSpan.FromHours(-3));

        Assert.Equal("mañana a las 06:45", StatusText.ResumeAt(until, now));
    }

    [Fact]
    public void ResumeAtNamesTheDateFurtherOut()
    {
        var now = new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.FromHours(-3));
        var until = new DateTimeOffset(2026, 9, 24, 6, 45, 0, TimeSpan.FromHours(-3));

        Assert.Equal("el 24/09 a las 06:45", StatusText.ResumeAt(until, now));
    }
}
