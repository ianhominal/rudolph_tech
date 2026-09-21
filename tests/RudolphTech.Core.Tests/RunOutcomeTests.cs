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
    public void APendingRunWithExitZeroAndNoStateMeansNothingWasPending()
    {
        // H-2 (blocking, second independent review): web/scripts/meli-survey.mjs's "--pending, nothing
        // due" path is the one place the script exits 0 without ever writing the state file. Reading
        // that triple (Pending, exit 0, state null) as Finished, via the plain exit-code fallback,
        // used to claim "Se relevaron 0 publicaciones de 0 producto(s)." on every ordinary silent tick,
        // not only after a wall.
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 0, null);

        Assert.Equal(RunOutcomeKind.Nothing, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
        Assert.DoesNotContain("0 producto", outcome.Message);
    }

    [Fact]
    public void ARealRunsCountsSurviveAFollowingSilentPendingTick()
    {
        // Mirrors AgentService.Remember's own gate (outcome.ShouldNotify): a no-op automatic pending
        // tick must not overwrite the settings window's last real run with "sin novedades" over real
        // counts. AgentService itself has no test harness (RudolphTech.Core.Tests never references
        // RudolphTech.csproj, the WPF project AgentService lives in), so this pins the exact contract
        // Remember must follow using the same Core types it actually reads and writes
        // (RunOutcome/RunSummary), simulating Remember's gate line for line.
        var real = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Finished, listings: 18));
        var lastRun = RunSummary.From(SurveyRunKind.Daily, DateTimeOffset.Now, DateTimeOffset.Now, real);

        var silentTick = RunOutcome.From(SurveyRunKind.Pending, 0, null);
        Assert.False(silentTick.ShouldNotify);
        if (silentTick.ShouldNotify) lastRun = RunSummary.From(SurveyRunKind.Pending, DateTimeOffset.Now, DateTimeOffset.Now, silentTick);

        Assert.Equal(18, lastRun.Listings);
        Assert.Equal(RunOutcomeKind.Finished, lastRun.Outcome);
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
        // M-1: From used to read DateTimeOffset.Now itself for the today/tomorrow/later phrasing, so
        // this test flaked between 22:00 and 23:59 local (the message would actually read "mañana")
        // and AResumesAtLaterTodayNamesTheTimeAloneInTheMessage flaked in the one millisecond window at
        // midnight. Both now pass now explicitly, pinned to a fixed instant.
        var now = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.FromHours(-3));
        var tomorrow = now.AddHours(7);
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), resumesAt: tomorrow, now: now);

        Assert.Contains("reanudan mañana a las", outcome.Message);
    }

    [Fact]
    public void AResumesAtLaterTodayNamesTheTimeAloneInTheMessage()
    {
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var soon = now.AddHours(2);
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), resumesAt: soon, now: now);

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
                if (code == 2)
                {
                    // L-5: the plain loop below always passes state: null, so on a Blocked exit code it
                    // only ever reaches the one generic "no reason recognised" sentence and never the
                    // three reason-specific sentences or the resume clause. Exercise every reason and a
                    // resumesAt in each of today/tomorrow/later so all of those get checked for dashes
                    // too, not only the one branch the plain loop happens to hit.
                    string?[] reasons = [null, "verification-timeout", "verification-cap", "verification-window-closed", "verification-no-window", "algo-que-este-build-no-reconoce"];
                    var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
                    DateTimeOffset?[] resumeTimes = [null, now.AddHours(2), now.AddHours(10), now.AddDays(3)];

                    foreach (var reason in reasons)
                    {
                        foreach (var resumesAt in resumeTimes)
                        {
                            AssertClean(RunOutcome.From(kind, code, State(SurveyStatus.Blocked, blockedReason: reason), resumesAt: resumesAt, now: now));
                        }
                    }
                    continue;
                }

                AssertClean(RunOutcome.From(kind, code, null));
            }
        }

        static void AssertClean(RunOutcome outcome)
        {
            Assert.False(string.IsNullOrWhiteSpace(outcome.Title));
            Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
            Assert.DoesNotContain('—', outcome.Message);
            Assert.DoesNotContain('–', outcome.Message);
        }
    }
}
