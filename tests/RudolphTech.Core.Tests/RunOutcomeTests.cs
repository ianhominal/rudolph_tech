using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// What the tray balloon says after a run, decided from the script's exit code plus the progress
/// file. Exit codes come from web/scripts/meli-survey.mjs: 0 finished, 2 blocked, 3 nothing to do,
/// anything else an error.
/// </summary>
public class RunOutcomeTests
{
    private static SurveyState State(SurveyStatus status, int listings = 0, string? message = null, string? blockedReason = null) => new()
    {
        Status = status,
        ProductIds = ["p1"],
        Done = 1,
        TotalListings = listings,
        Message = message,
        BlockedReason = blockedReason,
        StartedAt = new DateTimeOffset(2026, 9, 13, 9, 45, 0, TimeSpan.Zero),
        FinishedAt = new DateTimeOffset(2026, 9, 13, 10, 2, 0, TimeSpan.Zero),
    };

    [Fact]
    public void AFinishedRunReportsHowManyListingsItRead()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Finished, listings: 12));

        Assert.Equal(RunOutcomeKind.Finished, outcome.Kind);
        Assert.Equal(12, outcome.Listings);
        Assert.Contains("12", outcome.Message);
    }

    [Fact]
    public void ABlockedRunSaysMercadoLibreAskedForAVerification()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked));

        Assert.Equal(RunOutcomeKind.Blocked, outcome.Kind);
        Assert.Contains("verificación", outcome.Message);
    }

    [Fact]
    public void ExitCodeThreeOnAPendingRunMeansThereWasNothingToDo()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 3, null);

        Assert.Equal(RunOutcomeKind.Nothing, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
    }

    [Fact]
    public void AnErrorExitCodeIsAnError()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 1, null);

        Assert.Equal(RunOutcomeKind.Error, outcome.Kind);
        Assert.True(outcome.ShouldNotify);
    }

    [Fact]
    public void TheStateFileMessageIsCarriedIntoAnError()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 1, State(SurveyStatus.Error, message: "sin conexión"));

        Assert.Equal(RunOutcomeKind.Error, outcome.Kind);
        Assert.Contains("sin conexión", outcome.Message);
    }

    [Fact]
    public void ABlockedStateWinsOverASuccessfulExitCode()
    {
        // Not every exit 0 was written by this run: meli-survey.mjs's "--pending, nothing due" path
        // exits 0 without touching the state file, so a state left over from an earlier blocked run
        // can outlive it. RunOutcome.From must still read that file as blocked here; keeping a hold
        // from escalating on a read like this one is a separate concern, see BlockBackoff.Next (H1).
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Blocked));

        Assert.Equal(RunOutcomeKind.Blocked, outcome.Kind);
    }

    [Fact]
    public void AnInterruptedRunIsReportedAsSuch()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Manual, 1, State(SurveyStatus.Interrupted));

        Assert.Equal(RunOutcomeKind.Interrupted, outcome.Kind);
        Assert.Contains("interrump", outcome.Message);
    }

    [Fact]
    public void AManualRunThatFoundNothingStillNotifies()
    {
        // The person pressed "Relevar ahora" and is waiting for an answer, so silence is wrong.
        var outcome = RunOutcome.From(SurveyRunKind.Manual, 3, null);

        Assert.Equal(RunOutcomeKind.Nothing, outcome.Kind);
        Assert.True(outcome.ShouldNotify);
    }

    [Theory]
    [InlineData("verification-timeout")]
    [InlineData("verification-cap")]
    [InlineData("verification-window-closed")]
    public void ABlockedRunSharesOneSentenceForATimeoutACapOrTheWindowBeingClosed(string reason)
    {
        // TO-1: T3 (timed out), T6 (wall cap) and T9 (person closed the window) all share this sentence,
        // exactly as the texts table says "same as T3" for T6 and T9.
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: reason));

        Assert.Contains("La verificación de Mercado Libre quedó sin completar y el relevamiento se detuvo.", outcome.Message);
    }

    [Fact]
    public void ABlockedRunWhoseWindowCouldNotBeShownHasItsOwnSentence()
    {
        // T5: nobody even had the chance, which is a different claim from "quedó sin completar".
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: "verification-no-window"));

        Assert.Contains(
            "Mercado Libre pidió una verificación y la ventana de Chrome no se pudo mostrar en esta pantalla, así que nadie pudo completarla.",
            outcome.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("algo-que-este-build-no-reconoce")]
    public void ABlockedRunWithNoReasonOrAnUnrecognisedOneUsesTheGenericSentence(string? reason)
    {
        // Covers the old-script fallback (no blockedReason at all) and a future reason this build has
        // never heard of, with the same sentence: still true whatever wrote the state.
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: reason));

        Assert.Contains("Mercado Libre pidió una verificación y el relevamiento se detuvo.", outcome.Message);
    }

    [Fact]
    public void AResumesAtOfNullLeavesTheResumeClauseOut()
    {
        // A surface that does not know the retry time must not invent one.
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), resumesAt: null);

        Assert.DoesNotContain("reanudan", outcome.Message);
    }

    [Fact]
    public void AResumesAtTomorrowNamesTomorrowInTheMessage()
    {
        var tomorrow = DateTimeOffset.Now.AddDays(1);
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), resumesAt: tomorrow);

        Assert.Contains("reanudan mañana a las", outcome.Message);
    }

    [Fact]
    public void AResumesAtLaterTodayNamesTheTimeAloneInTheMessage()
    {
        var soon = DateTimeOffset.Now.AddHours(2);
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), resumesAt: soon);

        Assert.Contains($"reanudan a las {soon:HH:mm}", outcome.Message);
    }

    [Fact]
    public void TheThreeArgumentOverloadStillCompilesAndKeepsResolvingToBlocked()
    {
        // Regression pin: every existing 3-argument call site (including the private ones inside
        // AgentService's own failure paths) must keep compiling and keep landing on Blocked, with no
        // resume clause, since none of them know a resumesAt.
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked));

        Assert.Equal(RunOutcomeKind.Blocked, outcome.Kind);
        Assert.DoesNotContain("reanudan", outcome.Message);
    }

    [Fact]
    public void ResolveIsPublicSoAgentServiceCanPreviewTheOutcomeKindBeforeTheHoldExists()
    {
        Assert.Equal(RunOutcomeKind.Blocked, RunOutcome.Resolve(2, State(SurveyStatus.Blocked)));
        Assert.Equal(RunOutcomeKind.Nothing, RunOutcome.Resolve(3, null));
    }

    [Fact]
    public void EveryMessageIsWrittenInSpanishWithoutDashes()
    {
        SurveyRunKind[] kinds = [SurveyRunKind.Pending, SurveyRunKind.Daily, SurveyRunKind.Manual];
        int[] codes = [0, 1, 2, 3, 9];

        foreach (var kind in kinds)
        {
            foreach (var code in codes)
            {
                var outcome = RunOutcome.From(kind, code, null);
                Assert.False(string.IsNullOrWhiteSpace(outcome.Title));
                Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
                Assert.DoesNotContain('—', outcome.Message);
                Assert.DoesNotContain('–', outcome.Message);
            }
        }
    }
}
