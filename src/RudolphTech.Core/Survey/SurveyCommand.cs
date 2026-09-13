namespace RudolphTech.Core.Survey;

/// <summary>
/// The command line for each kind of run, the same one the old agent's .bat files used:
/// "--pending" every few minutes, "--all" once a day, and "--all --force" when somebody presses
/// "Relevar ahora" (force, so a snapshot already captured today is taken again instead of skipped).
/// </summary>
public static class SurveyCommand
{
    public static IReadOnlyList<string> BuildArguments(SurveyRunKind kind, string scriptPath) => kind switch
    {
        SurveyRunKind.Pending => [scriptPath, "--pending"],
        SurveyRunKind.Daily => [scriptPath, "--all"],
        _ => [scriptPath, "--all", "--force"],
    };

    /// <summary> playwright-core is the agent package's only dependency; nothing else is needed to run. </summary>
    public static IReadOnlyList<string> NpmInstallArguments() => ["install", "--omit=dev", "--no-audit", "--no-fund"];
}
