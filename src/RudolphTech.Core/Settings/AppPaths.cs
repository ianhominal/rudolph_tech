namespace RudolphTech.Core.Settings;

/// <summary>
/// Where the app keeps its own files: always under %LocalAppData%, never next to the executable in
/// Program Files, which the user's account cannot write to.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "RudolphTech";

    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary> Where the downloaded survey scripts and their node_modules live. </summary>
    public static string AgentFolder => Path.Combine(Root, "agent");

    public static string LogFolder => Path.Combine(Root, "logs");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary> The progress file the survey script writes, relative to the agent folder. </summary>
    public static string StateFile => Path.Combine(AgentFolder, "data", "competencia", "relevamiento-estado.json");

    public static string SurveyScript => Path.Combine(AgentFolder, "meli-survey.mjs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AgentFolder);
        Directory.CreateDirectory(LogFolder);
    }
}
