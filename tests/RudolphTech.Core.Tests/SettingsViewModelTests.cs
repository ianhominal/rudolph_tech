using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The settings window's Estado headline, its muted subtitle, and which groups are enabled, all
/// decided as pure functions of the current state so the window itself has nothing to get wrong.
/// </summary>
public class SettingsViewModelTests
{
    private static SettingsViewModelInputs Inputs(
        bool linked = true,
        bool running = false,
        RunSummary? lastRun = null,
        bool dailyEnabled = true,
        bool pendingEnabled = true,
        bool nodeAvailable = true) => new()
        {
            Linked = linked,
            Running = running,
            LastRun = lastRun,
            DailyEnabled = dailyEnabled,
            PendingEnabled = pendingEnabled,
            NodeAvailable = nodeAvailable,
        };

    [Fact]
    public void SaysEverythingIsReadyWhenLinked()
    {
        Assert.Equal(
            "Todo listo. Rudolph Tech releva desde esta PC.",
            SettingsViewModel.Headline(Inputs(linked: true)));
    }

    [Fact]
    public void SaysWhatIsMissingWhenNotLinked()
    {
        Assert.Equal(
            "Falta vincular esta PC con la aplicación.",
            SettingsViewModel.Headline(Inputs(linked: false)));
    }

    [Fact]
    public void SaysNothingWasRelevedYetWhenThereIsNoLastRun()
    {
        Assert.Equal("Todavía no relevó nada.", SettingsViewModel.LastRunLine(Inputs(lastRun: null)));
    }

    [Fact]
    public void ExplainsToLinkInsteadOfTheLastRunWhenNotLinked()
    {
        var summary = new RunSummary { Kind = SurveyRunKind.Daily, StartedAt = DateTimeOffset.Now, Outcome = RunOutcomeKind.Finished };

        Assert.Equal("Vinculá esta PC para que pueda relevar.", SettingsViewModel.LastRunLine(Inputs(linked: false, lastRun: summary)));
    }

    [Fact]
    public void SummarisesAFinishedRun()
    {
        var summary = new RunSummary
        {
            Kind = SurveyRunKind.Daily,
            StartedAt = new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.FromHours(-3)),
            FinishedAt = new DateTimeOffset(2026, 9, 13, 6, 50, 0, TimeSpan.FromHours(-3)),
            Outcome = RunOutcomeKind.Finished,
            Products = 4,
            Listings = 20,
        };

        var text = SettingsViewModel.LastRunLine(Inputs(lastRun: summary));

        Assert.Contains("13/09", text);
        Assert.Contains("06:50", text);
        Assert.Contains("4 producto(s)", text);
        Assert.Contains("sin errores", text);
    }

    [Theory]
    [InlineData(RunOutcomeKind.Nothing, "sin novedades")]
    [InlineData(RunOutcomeKind.Blocked, "necesitó una verificación")]
    [InlineData(RunOutcomeKind.Interrupted, "interrumpido")]
    [InlineData(RunOutcomeKind.Error, "con errores")]
    public void DescribesEveryOtherOutcome(RunOutcomeKind outcome, string expectedPhrase)
    {
        var summary = new RunSummary
        {
            Kind = SurveyRunKind.Pending,
            StartedAt = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.FromHours(-3)),
            FinishedAt = new DateTimeOffset(2026, 9, 13, 8, 1, 0, TimeSpan.FromHours(-3)),
            Outcome = outcome,
        };

        Assert.Contains(expectedPhrase, SettingsViewModel.LastRunLine(Inputs(lastRun: summary)));
    }

    [Fact]
    public void FallsBackToTheStartTimeWhenTheRunNeverFinished()
    {
        var summary = new RunSummary
        {
            Kind = SurveyRunKind.Daily,
            StartedAt = new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.FromHours(-3)),
            FinishedAt = null,
            Outcome = RunOutcomeKind.Interrupted,
        };

        Assert.Contains("06:45", SettingsViewModel.LastRunLine(Inputs(lastRun: summary)));
    }

    [Fact]
    public void RunNowIsEnabledWhenLinkedAndIdle()
    {
        Assert.True(SettingsViewModel.RunNowEnabled(Inputs(linked: true, running: false)));
    }

    [Fact]
    public void RunNowIsDisabledWhileRunning()
    {
        Assert.False(SettingsViewModel.RunNowEnabled(Inputs(linked: true, running: true)));
    }

    [Fact]
    public void RunNowIsDisabledWhenNotLinked()
    {
        Assert.False(SettingsViewModel.RunNowEnabled(Inputs(linked: false, running: false)));
    }

    [Fact]
    public void RunNowTextShowsProgressWhileRunning()
    {
        Assert.Equal("Relevando...", SettingsViewModel.RunNowText(Inputs(running: true)));
        Assert.Equal("Relevar ahora", SettingsViewModel.RunNowText(Inputs(running: false)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheScheduleGroupIsEnabledOnlyWhenLinked(bool linked, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.ScheduleGroupEnabled(Inputs(linked: linked)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheOptionsGroupIsEnabledOnlyWhenLinked(bool linked, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.OptionsGroupEnabled(Inputs(linked: linked)));
    }

    [Fact]
    public void GroupEnablementIgnoresTheDailyAndPendingSwitches()
    {
        // Turning a schedule off is still a linked, fully usable settings window: only whether the
        // app is linked at all should gate the groups, not what the person chose inside them.
        var inputs = Inputs(linked: true, dailyEnabled: false, pendingEnabled: false);

        Assert.True(SettingsViewModel.ScheduleGroupEnabled(inputs));
        Assert.True(SettingsViewModel.OptionsGroupEnabled(inputs));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TheDailyTimePickerNeedsBothLinkedAndDailyEnabled(bool linked, bool dailyEnabled, bool expected)
    {
        var inputs = Inputs(linked: linked, dailyEnabled: dailyEnabled);

        Assert.Equal(expected, SettingsViewModel.DailyTimeEnabled(inputs));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ThePendingMinutesControlNeedsBothLinkedAndPendingEnabled(bool linked, bool pendingEnabled, bool expected)
    {
        var inputs = Inputs(linked: linked, pendingEnabled: pendingEnabled);

        Assert.Equal(expected, SettingsViewModel.PendingControlsEnabled(inputs));
    }

    [Fact]
    public void RunNowIsDisabledWhenTheBundledNodeIsMissingEvenIfLinkedAndIdle()
    {
        Assert.False(SettingsViewModel.RunNowEnabled(Inputs(linked: true, running: false, nodeAvailable: false)));
    }

    [Fact]
    public void RunNowTooltipCarriesTheMissingNodeMessageOnlyWhileItIsMissing()
    {
        Assert.Equal(SettingsViewModel.NodeMissingMessage, SettingsViewModel.RunNowTooltip(Inputs(nodeAvailable: false)));
        Assert.Equal("", SettingsViewModel.RunNowTooltip(Inputs(nodeAvailable: true)));
    }

    [Fact]
    public void TheStatusLineWarnsAboutTheMissingNodeOnlyOnceLinked()
    {
        Assert.Equal(SettingsViewModel.NodeMissingMessage, SettingsViewModel.NodeMissingStatus(Inputs(linked: true, nodeAvailable: false)));
        Assert.Null(SettingsViewModel.NodeMissingStatus(Inputs(linked: false, nodeAvailable: false)));
        Assert.Null(SettingsViewModel.NodeMissingStatus(Inputs(linked: true, nodeAvailable: true)));
    }

    [Fact]
    public void PrimaryActionOffersToLinkWhenNotLinked()
    {
        Assert.Equal("Vincular esta PC", SettingsViewModel.PrimaryActionText(Inputs(linked: false)));
    }

    [Fact]
    public void PrimaryActionFallsBackToRunNowTextWhenLinked()
    {
        Assert.Equal("Relevar ahora", SettingsViewModel.PrimaryActionText(Inputs(linked: true, running: false)));
        Assert.Equal("Relevando...", SettingsViewModel.PrimaryActionText(Inputs(linked: true, running: true)));
    }

    [Theory]
    [InlineData(false, false, true)] // not linked, idle: can link
    [InlineData(false, true, false)] // not linked, a survey is running: wait
    public void LinkingNeedsNoNodeUnlikeRunNow(bool linked, bool running, bool expected)
    {
        // nodeAvailable: false on purpose, to prove linking does not depend on it the way "Relevar
        // ahora" does.
        Assert.Equal(expected, SettingsViewModel.PrimaryActionEnabled(Inputs(linked: linked, running: running, nodeAvailable: false)));
    }

    [Fact]
    public void PrimaryActionEnabledMatchesRunNowEnabledWhenLinked()
    {
        Assert.Equal(
            SettingsViewModel.RunNowEnabled(Inputs(linked: true, running: false, nodeAvailable: false)),
            SettingsViewModel.PrimaryActionEnabled(Inputs(linked: true, running: false, nodeAvailable: false)));
    }

    [Fact]
    public void PrimaryActionTooltipNeverMentionsNodeWhileNotLinked()
    {
        Assert.Equal("", SettingsViewModel.PrimaryActionTooltip(Inputs(linked: false, nodeAvailable: false)));
    }

    [Fact]
    public void PrimaryActionTooltipMatchesRunNowTooltipWhenLinked()
    {
        Assert.Equal(SettingsViewModel.NodeMissingMessage, SettingsViewModel.PrimaryActionTooltip(Inputs(linked: true, nodeAvailable: false)));
        Assert.Equal("", SettingsViewModel.PrimaryActionTooltip(Inputs(linked: true, nodeAvailable: true)));
    }

    [Fact]
    public void LinkSummaryNeverSaysVinculadaWhenNotLinked()
    {
        var text = SettingsViewModel.LinkSummary(Inputs(linked: false), "https://rudolph-mvp.vercel.app", null);

        Assert.DoesNotContain("Vinculada", text);
        Assert.Equal("Se vinculará con https://rudolph-mvp.vercel.app.", text);
    }

    [Fact]
    public void LinkSummarySaysNoAddressAtAllWhenNoneIsConfiguredAndNotLinked()
    {
        Assert.Equal(SettingsViewModel.NoAddressConfigured, SettingsViewModel.LinkSummary(Inputs(linked: false), null, null));
        Assert.Equal(SettingsViewModel.NoAddressConfigured, SettingsViewModel.LinkSummary(Inputs(linked: false), "   ", null));
    }

    [Fact]
    public void LinkSummarySaysVinculadaOnlyOnceActuallyLinked()
    {
        var text = SettingsViewModel.LinkSummary(Inputs(linked: true), "https://rudolph-mvp.vercel.app", null);

        Assert.StartsWith("Vinculada con https://rudolph-mvp.vercel.app.", text);
        Assert.Contains("El agente todavía no se descargó.", text);
    }

    [Fact]
    public void LinkSummaryMentionsTheAgentDateOnceLinkedAndDownloaded()
    {
        var downloaded = new DateTimeOffset(2026, 9, 11, 9, 48, 0, TimeSpan.FromHours(-3));

        var text = SettingsViewModel.LinkSummary(Inputs(linked: true), "https://rudolph-mvp.vercel.app", downloaded);

        Assert.Contains("Agente actualizado el 11/09/2026 09:48", text);
    }
}
