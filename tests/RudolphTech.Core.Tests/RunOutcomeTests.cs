using RudolphTech.Core.Scheduling;
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
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Finished, listings: 12), DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Finished, outcome.Kind);
        Assert.Equal(12, outcome.Listings);
        Assert.Contains("12", outcome.Message);
    }

    [Fact]
    public void ABlockedRunSaysMercadoLibreAskedForAVerification()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked), DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Blocked, outcome.Kind);
        Assert.Contains("verificación", outcome.Message);
    }

    [Fact]
    public void ExitCodeThreeOnAPendingRunMeansThereWasNothingToDo()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 3, null, DateTimeOffset.Now);

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
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 0, null, DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Nothing, outcome.Kind);
        Assert.False(outcome.ShouldNotify);
        Assert.DoesNotContain("0 producto", outcome.Message);
    }

    [Fact]
    public void ARealRunsCountsSurviveAFollowingSilentPendingTick()
    {
        // Mirrors AgentService.Remember's own gate: a no-op automatic pending tick with no state file
        // of its own (the "--pending, nothing due" path, H-2) must not overwrite the settings window's
        // last real run with "sin novedades" over real counts. AgentService itself has no test harness
        // (RudolphTech.Core.Tests never references RudolphTech.csproj, the WPF project AgentService
        // lives in), so this pins the exact contract Remember must follow using the same Core types it
        // actually reads and writes (RunOutcome/RunSummary), simulating Remember's gate line for line.
        // The gate is outcome.Kind != Nothing || stateIsFromThisRun, not outcome.ShouldNotify alone
        // (fix pass 2, item 2, medium): see AServedPendingRequestThatFoundNoNewListingsStillKeepsItsRecord
        // just below for the case that tells the two apart.
        var real = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Finished, listings: 18), DateTimeOffset.Now);
        var lastRun = RunSummary.From(SurveyRunKind.Daily, DateTimeOffset.Now, DateTimeOffset.Now, real);

        var silentTick = RunOutcome.From(SurveyRunKind.Pending, 0, null, DateTimeOffset.Now);
        const bool stateIsFromThisRun = false;
        Assert.False(silentTick.Kind != RunOutcomeKind.Nothing || stateIsFromThisRun);
        if (silentTick.Kind != RunOutcomeKind.Nothing || stateIsFromThisRun)
            lastRun = RunSummary.From(SurveyRunKind.Pending, DateTimeOffset.Now, DateTimeOffset.Now, silentTick);

        Assert.Equal(18, lastRun.Listings);
        Assert.Equal(RunOutcomeKind.Finished, lastRun.Outcome);
    }

    [Fact]
    public void AServedPendingRequestThatFoundNoNewListingsStillKeepsItsRecord()
    {
        // MEDIUM (fix pass 2, item 2): meli-survey.mjs:1559 can serve a --pending request and write the
        // state file even when every product already had today's snapshot, exiting 3 (nothing captured
        // this time, not nothing due). RunOutcome.From/Resolve read that combination as
        // RunOutcomeKind.Nothing, the same Kind as the completely different "--pending, nothing due"
        // case (H-2) that never touches the state file at all, so gating Settings.LastRun on
        // outcome.ShouldNotify alone, as the first fix pass did, dropped the record of a request that
        // WAS served: a person pressing "Actualizar" on the web for a product already surveyed today
        // would see the settings window still showing an older run. Mirrors AgentService.Remember's
        // gate line for line, same reason ARealRunsCountsSurviveAFollowingSilentPendingTick does.
        var served = RunOutcome.From(SurveyRunKind.Pending, 3, State(SurveyStatus.Finished, listings: 0), DateTimeOffset.Now);
        Assert.False(served.ShouldNotify);

        RunSummary? lastRun = null;
        const bool stateIsFromThisRun = true;
        if (served.Kind != RunOutcomeKind.Nothing || stateIsFromThisRun)
            lastRun = RunSummary.From(SurveyRunKind.Pending, DateTimeOffset.Now, DateTimeOffset.Now, served);

        Assert.NotNull(lastRun);
        Assert.Equal(RunOutcomeKind.Nothing, lastRun!.Outcome);
    }

    [Fact]
    public void AnErrorExitCodeIsAnError()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 1, null, DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Error, outcome.Kind);
        Assert.True(outcome.ShouldNotify);
    }

    [Fact]
    public void TheStateFileMessageIsCarriedIntoAnError()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 1, State(SurveyStatus.Error, message: "sin conexión"), DateTimeOffset.Now);

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
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 0, State(SurveyStatus.Blocked), DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Blocked, outcome.Kind);
    }

    [Fact]
    public void AnInterruptedRunIsReportedAsSuch()
    {
        var outcome = RunOutcome.From(SurveyRunKind.Manual, 1, State(SurveyStatus.Interrupted), DateTimeOffset.Now);

        Assert.Equal(RunOutcomeKind.Interrupted, outcome.Kind);
        Assert.Contains("interrump", outcome.Message);
    }

    [Fact]
    public void AManualRunThatFoundNothingStillNotifies()
    {
        // The person pressed "Relevar ahora" and is waiting for an answer, so silence is wrong.
        var outcome = RunOutcome.From(SurveyRunKind.Manual, 3, null, DateTimeOffset.Now);

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
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: reason), DateTimeOffset.Now);

        Assert.Contains("La verificación de Mercado Libre quedó sin completar y el relevamiento se detuvo.", outcome.Message);
    }

    [Fact]
    public void ABlockedRunWhoseWindowCouldNotBeShownHasItsOwnSentence()
    {
        // T5: nobody even had the chance, which is a different claim from "quedó sin completar".
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: "verification-no-window"), DateTimeOffset.Now);

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
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked, blockedReason: reason), DateTimeOffset.Now);

        Assert.Contains("Mercado Libre pidió una verificación y el relevamiento se detuvo.", outcome.Message);
    }

    [Fact]
    public void AResumesAtOfNullLeavesTheResumeClauseOut()
    {
        // A surface that does not know the retry time must not invent one.
        var outcome = RunOutcome.From(SurveyRunKind.Pending, 2, State(SurveyStatus.Blocked), DateTimeOffset.Now, resumesAt: null);

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
    public void AResumesAtPreviewFallsBackToTheHeldHoldWhenTheDailyCandidateIsAlreadyPast()
    {
        // HIGH, blocking (fix pass 2, item 1): AgentService.ResumesAt asks ScheduleDecider.NextRunAt
        // what the effective next automatic run is once the previewed hold is in force, but that
        // question is answered with Settings.LastDailyRun as it stands right now, still yesterday's
        // date at preview time (Remember has not written today's run yet). For a DAILY run already past
        // its own time when the script exits (06:45 walled, exits at 06:50) NextRunAt honestly reports
        // the daily time already due, 06:45, now in the past; the first fix pass's own guess
        // ("?? previewedHold.Until") never triggers here because 06:45 is not null, so it named 06:45
        // to a person as the resume time, five minutes after it had already passed. AgentService.
        // ResumesAt has no test harness of its own (see RunSurveyCoreAsync's own note), so this mirrors
        // its exact formula, using only RudolphTech.Core types, the same way
        // ARealRunsCountsSurviveAFollowingSilentPendingTick mirrors Remember's gate.
        var now = new DateTimeOffset(2026, 9, 14, 6, 50, 0, TimeSpan.FromHours(-3));
        var previewedHoldUntil = now.AddHours(2);
        var scheduleInputs = new ScheduleInputs
        {
            Now = now,
            PendingIntervalMinutes = 15,
            DailyTime = new TimeOnly(6, 45),
            Configured = true,
            LastPendingRun = now,
            LastDailyRun = DateOnly.FromDateTime(now.Date.AddDays(-1)),
            BlockedUntil = previewedHoldUntil,
        };

        var next = ScheduleDecider.NextRunAt(scheduleInputs);
        Assert.True(next < now);

        DateTimeOffset? resumesAt = next switch
        {
            null => null,
            { } at when at > now => at,
            _ => previewedHoldUntil,
        };
        Assert.Equal(previewedHoldUntil, resumesAt);

        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked), resumesAt: resumesAt, now: now);

        Assert.Contains($"reanudan a las {previewedHoldUntil:HH:mm}", outcome.Message);
        Assert.DoesNotContain("a las 06:45", outcome.Message);
    }

    [Fact]
    public void AResumesAtPreviewHonoursAnEarlierDailyCandidateThatIsStillAheadOfNow()
    {
        // Non-regression pin for the scenario the first fix pass's own H-1 fix already covered
        // correctly: a pending wall at 05:00 persists a hold to 07:00, but the daily survey at 06:45 is
        // sooner and still ahead of now, so the guard above must trust NextRunAt's own candidate rather
        // than always falling back to the raw hold.
        var now = new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.FromHours(-3));
        var previewedHoldUntil = new DateTimeOffset(2026, 9, 21, 7, 0, 0, TimeSpan.FromHours(-3));
        var scheduleInputs = new ScheduleInputs
        {
            Now = now,
            PendingIntervalMinutes = 15,
            DailyTime = new TimeOnly(6, 45),
            Configured = true,
            LastPendingRun = now,
            LastDailyRun = DateOnly.FromDateTime(now.Date.AddDays(-1)),
            BlockedUntil = previewedHoldUntil,
        };

        var next = ScheduleDecider.NextRunAt(scheduleInputs);
        Assert.True(next > now);

        DateTimeOffset? resumesAt = next switch
        {
            null => null,
            { } at when at > now => at,
            _ => previewedHoldUntil,
        };
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 6, 45, 0, TimeSpan.FromHours(-3)), resumesAt);

        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked), resumesAt: resumesAt, now: now);

        Assert.Contains("reanudan a las 06:45", outcome.Message);
    }

    [Fact]
    public void AResumesAtPreviewNamesNoTimeAtAllWhenNothingAutomaticIsScheduled()
    {
        // LOW (fix pass 2, item 3): reproduced with a manual "Relevar ahora" run pressed while paused,
        // which bypasses ScheduleDecider.Decide entirely and can still hit a verification wall.
        // ResumesAt used to fall back to previewedHold.Until whenever NextRunAt returned null for any
        // reason, including Paused, so the balloon promised "se reanudan a las 16:00" while nothing
        // automatic was ever going to run; the tooltip in the very same state correctly says "Rudolph
        // Tech: en pausa". Mirrors AgentService.ResumesAt's exact formula, same pattern as
        // AResumesAtPreviewFallsBackToTheHeldHoldWhenTheDailyCandidateIsAlreadyPast above.
        var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(-3));
        var previewedHoldUntil = new DateTimeOffset(2026, 9, 21, 16, 0, 0, TimeSpan.FromHours(-3));
        var scheduleInputs = new ScheduleInputs
        {
            Now = now,
            PendingIntervalMinutes = 15,
            DailyTime = new TimeOnly(6, 45),
            Paused = true,
            Configured = true,
            LastPendingRun = now,
            LastDailyRun = DateOnly.FromDateTime(now.Date.AddDays(-1)),
            BlockedUntil = previewedHoldUntil,
        };

        var next = ScheduleDecider.NextRunAt(scheduleInputs);
        Assert.Null(next);

        DateTimeOffset? resumesAt = next switch
        {
            null => null,
            { } at when at > now => at,
            _ => previewedHoldUntil,
        };
        Assert.Null(resumesAt);

        var outcome = RunOutcome.From(SurveyRunKind.Manual, 2, State(SurveyStatus.Blocked), resumesAt: resumesAt, now: now);

        Assert.DoesNotContain("reanudan", outcome.Message);
    }

    [Fact]
    public void ACallWithNoResumesAtStillCompilesAndKeepsResolvingToBlocked()
    {
        // Regression pin, updated for fix pass 2, item 4 (low): now used to default to
        // DateTimeOffset.Now, the same as resumesAt still does today, a latent trap for any future
        // Blocked path using this short overload (a caller reading the clock itself would reintroduce
        // the exact flakiness M-1 closed for BlockedMessage's own now parameter). now is required from
        // here on; resumesAt stays the only optional argument, and every call site that still omits it,
        // including the private ones inside AgentService's own failure paths, must keep compiling and
        // keep landing on Blocked with no resume clause.
        var outcome = RunOutcome.From(SurveyRunKind.Daily, 2, State(SurveyStatus.Blocked), DateTimeOffset.Now);

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

                AssertClean(RunOutcome.From(kind, code, null, DateTimeOffset.Now));
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
