using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary> The exact command line each kind of run hands to node, matching what the old .bat files did. </summary>
public class SurveyCommandTests
{
    [Fact]
    public void TheScheduledPendingCheckAsksOnlyForWhatIsPending()
    {
        var arguments = SurveyCommand.BuildArguments(SurveyRunKind.Pending, @"C:\agente\meli-survey.mjs");

        Assert.Equal([@"C:\agente\meli-survey.mjs", "--pending"], arguments);
    }

    [Fact]
    public void TheDailyRunSurveysEverything()
    {
        var arguments = SurveyCommand.BuildArguments(SurveyRunKind.Daily, @"C:\agente\meli-survey.mjs");

        Assert.Equal([@"C:\agente\meli-survey.mjs", "--all"], arguments);
    }

    [Fact]
    public void RelevarAhoraRedoesTodaysCaptureInstead0fSkippingIt()
    {
        var arguments = SurveyCommand.BuildArguments(SurveyRunKind.Manual, @"C:\agente\meli-survey.mjs");

        Assert.Equal([@"C:\agente\meli-survey.mjs", "--all", "--force"], arguments);
    }

    [Fact]
    public void TheNpmInstallOnlyBringsWhatIsNeededToRun()
    {
        Assert.Equal(["install", "--omit=dev", "--no-audit", "--no-fund"], SurveyCommand.NpmInstallArguments());
    }
}
