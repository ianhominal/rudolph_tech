using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RudolphTech.Core.Logging;

/// <summary>
/// One log file per day under %LocalAppData%\RudolphTech\logs, holding both what the tray app did
/// and every line the survey script printed. A detached node process has nowhere else to write, and
/// "Ver registro" in the tray menu simply opens this folder.
/// </summary>
public sealed partial class LogWriter
{
    public const int DefaultKeepDays = 30;

    private static readonly object Gate = new();

    /// <summary>
    /// With the byte order mark: the person reading a log opens it with whatever Windows tool is at
    /// hand, and without it some of them show the accents as garbage. Only written when the file is
    /// created, since appending to a non empty file skips the preamble.
    /// </summary>
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly string _folder;

    public LogWriter(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public string Folder => _folder;

    public string CurrentFile => Path.Combine(_folder, FileNameFor(DateTimeOffset.Now));

    public static string FileNameFor(DateTimeOffset moment) => $"agent-{moment:yyyy-MM-dd}.log";

    [GeneratedRegex(@"^agent-(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern { get; }

    /// <summary> Appends one timestamped line. Never throws: losing a log line must not stop a survey. </summary>
    public void Write(string line)
    {
        var text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}";
        lock (Gate)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.AppendAllText(CurrentFile, text, FileEncoding);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(30); // another process holds the file open for a moment
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }
    }

    /// <summary> Writes a line of output produced by the survey script, without a second timestamp prefix of our own. </summary>
    public void WriteScriptOutput(string line) => Write("  " + line);

    public static void Prune(string folder, DateTimeOffset now, int keepDays = DefaultKeepDays)
    {
        if (!Directory.Exists(folder)) return;

        var cutoff = DateOnly.FromDateTime(now.Date).AddDays(-keepDays);
        foreach (var file in Directory.GetFiles(folder, "agent-*.log"))
        {
            var match = FileNamePattern.Match(Path.GetFileName(file));
            if (!match.Success) continue;
            if (!DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            if (day >= cutoff) continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Kept for another day; not worth telling anybody about.
            }
        }
    }
}
