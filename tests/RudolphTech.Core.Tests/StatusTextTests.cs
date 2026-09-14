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

        var text = StatusText.Schedule(settings, paused: false);

        Assert.Contains("15", text);
        Assert.Contains("06:45", text);
    }

    [Fact]
    public void SaysThatEverythingIsPaused()
    {
        var settings = new AppSettings();

        Assert.Contains("pausa", StatusText.Schedule(settings, paused: true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTrayTooltipFitsInWindowsSixtyThreeCharacterLimit()
    {
        var settings = new AppSettings { PendingIntervalMinutes = 120, DailyTime = new TimeOnly(23, 59) };

        Assert.InRange(StatusText.TrayTooltip(settings, paused: false, running: true).Length, 1, 63);
        Assert.InRange(StatusText.TrayTooltip(settings, paused: true, running: false).Length, 1, 63);
    }
}
