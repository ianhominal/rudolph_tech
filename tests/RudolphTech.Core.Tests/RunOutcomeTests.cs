using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// What the tray balloon says after a run, decided from the script's exit code plus the progress
/// file. Exit codes come from web/scripts/meli-survey.mjs: 0 finished, 2 blocked, 3 nothing to do,
/// anything else an error.
/// </summary>
public class RunOutcomeTests
{
    private static SurveyState State(SurveyStatus status, int listings = 0, string? message = null) => new()
    {
        Status = status,
        ProductIds = ["p1"],
        Done = 1,
        TotalListings = listings,
        Message = message,
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
