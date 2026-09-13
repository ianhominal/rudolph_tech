using System.Text;
using System.Text.RegularExpressions;

namespace RudolphTech.Core.Agent;

/// <summary>
/// The three settings the survey script reads (web/scripts/meli-lib.mjs loadEnv): RUDOLPH_APP_URL,
/// SURVEY_INGEST_TOKEN and the optional SURVEY_CHROME_MINIMIZED.
///
/// The tray app reads them out of the .env that comes inside the downloaded package, keeps the
/// token protected in settings.json, and hands the values back to the script in two ways at once:
/// as real environment variables of the child process (which loadEnv layers on top of any file, so
/// they always win) and as a plain .env written right before the run and deleted right after, which
/// is what the script falls back to if it is ever launched by hand from that folder.
/// </summary>
public static partial class AgentEnvironment
{
    public const string AppUrlKey = "RUDOLPH_APP_URL";
    public const string IngestTokenKey = "SURVEY_INGEST_TOKEN";
    public const string ChromeMinimizedKey = "SURVEY_CHROME_MINIMIZED";
    public const string FileName = ".env";

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex LinePattern { get; }

    /// <summary> Same rules as meli-lib.mjs loadEnv: KEY=value lines, optional surrounding quotes, anything else ignored. </summary>
    public static Dictionary<string, string> Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        // A byte order mark would otherwise glue itself to the first key and lose that whole line.
        foreach (var line in content.TrimStart('\uFEFF').Split('\n'))
        {
            var match = LinePattern.Match(line.TrimEnd('\r'));
            if (!match.Success) continue;
            values[match.Groups[1].Value] = match.Groups[2].Value.Trim('"', '\'');
        }
        return values;
    }

    public static string Build(string appUrl, string ingestToken, bool chromeOffScreen) =>
        string.Join("\n",
            "# Generado por Rudolph Tech para esta corrida. No compartir este archivo.",
            $"{AppUrlKey}={appUrl}",
            $"{IngestTokenKey}={ingestToken}",
            $"{ChromeMinimizedKey}={(chromeOffScreen ? "1" : "")}",
            "");

    /// <summary> The same values as environment variables of the child process, where no file can leak them. </summary>
    public static Dictionary<string, string> BuildProcessVariables(string appUrl, string ingestToken, bool chromeOffScreen)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppUrlKey] = appUrl,
            [IngestTokenKey] = ingestToken,
        };
        if (chromeOffScreen) variables[ChromeMinimizedKey] = "1";
        return variables;
    }

    /// <summary> Writes the .env next to the scripts. Deleted again by <see cref="AgentPackageInstaller.RemoveEnvFile"/>. </summary>
    public static void WriteFile(string agentFolder, string appUrl, string ingestToken, bool chromeOffScreen)
    {
        Directory.CreateDirectory(agentFolder);
        File.WriteAllText(Path.Combine(agentFolder, FileName), Build(appUrl, ingestToken, chromeOffScreen), new UTF8Encoding(false));
    }
}
