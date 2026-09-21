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

        var text = StatusText.Schedule(settings, paused: false, DateTimeOffset.Now);

        Assert.Contains("15", text);
        Assert.Contains("06:45", text);
    }

    [Fact]
    public void SaysThatEverythingIsPaused()
    {
        var settings = new AppSettings();

        Assert.Contains("pausa", StatusText.Schedule(settings, paused: true, DateTimeOffset.Now), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScheduleBalloonDoesNotPromiseAFiveMinuteCheckDuringAHold()
    {
        // Found in the independent review of A1: after a wall, this balloon (shown when Reanudar is
        // pressed) kept saying "Atiende pedidos cada 5 minutos" even though BlockBackoff was holding
        // every automatic pending check off for hours. A claim the app cannot back, same standing rule
        // as the tooltip.
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings
        {
            PendingIntervalMinutes = 5,
            BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3)),
        };

        var text = StatusText.Schedule(settings, paused: false, now);

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

        var text = StatusText.Schedule(settings, paused: false, now);

        Assert.Contains("cada", text);
    }

    [Fact]
    public void TheTrayTooltipFitsInWindowsSixtyThreeCharacterLimit()
    {
        var settings = new AppSettings { PendingIntervalMinutes = 120, DailyTime = new TimeOnly(23, 59) };
        var now = DateTimeOffset.Now;

        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: true, now).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: true, running: false, now).Length, 1, 63);
    }

    [Fact]
    public void TheTooltipDuringAWaitAsksToVerifyInChrome()
    {
        // TO-2/BB-8's text (T1): a live sub-state of "running", so it has to win over the plain
        // "relevando ahora" the same running flag would otherwise show.
        var settings = new AppSettings();
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));

        var text = StatusText.TrayTooltip(settings, paused: false, running: true, now, waiting: true);

        Assert.Equal("Rudolph Tech: hay que verificar en la ventana de Chrome", text);
    }

    [Fact]
    public void TheTooltipDuringAHoldThatEndsTodaySaysTheTime()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = new DateTimeOffset(2026, 9, 21, 16, 40, 0, TimeSpan.FromHours(-3)) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now);

        Assert.Equal("Rudolph Tech: sin relevar hasta las 16:40", text);
    }

    [Fact]
    public void TheTooltipDuringAHoldThatEndsTomorrowSaysManana()
    {
        var now = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = new DateTimeOffset(2026, 9, 22, 6, 45, 0, TimeSpan.FromHours(-3)) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now);

        Assert.Equal("Rudolph Tech: sin relevar hasta mañana 06:45", text);
    }

    [Fact]
    public void PauseWinsOverAHeldTooltip()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = now.AddHours(2) };

        var text = StatusText.TrayTooltip(settings, paused: true, running: false, now);

        Assert.Equal("Rudolph Tech: en pausa", text);
    }

    [Fact]
    public void TheTooltipWhileRunningIgnoresAnyHold()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { BlockedUntil = now.AddHours(2) };

        var text = StatusText.TrayTooltip(settings, paused: false, running: true, now);

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

        var text = StatusText.TrayTooltip(settings, paused: false, running: false, now);

        Assert.Contains("diario", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTrayTooltipBranchFitsInSixtyThreeCharactersEvenWithAFarOffHold(bool waiting)
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var settings = new AppSettings { PendingIntervalMinutes = 120, DailyTime = new TimeOnly(23, 59), BlockedUntil = now.AddDays(3) };

        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: true, now, waiting).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: true, running: false, now, waiting).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: false, now, waiting).Length, 1, 63);
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
