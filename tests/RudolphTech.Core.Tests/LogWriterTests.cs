using RudolphTech.Core.Logging;

namespace RudolphTech.Core.Tests;

/// <summary> The rolling log under %LocalAppData%\RudolphTech\logs, one file per day. </summary>
public class LogWriterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rudolph-tech-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OneFilePerDay()
    {
        Assert.Equal("agent-2026-09-13.log", LogWriter.FileNameFor(new DateTimeOffset(2026, 9, 13, 22, 0, 0, TimeSpan.FromHours(-3))));
    }

    [Fact]
    public void AppendsTimestampedLines()
    {
        var writer = new LogWriter(_folder);

        writer.Write("primera linea");
        writer.Write("segunda linea");

        var content = File.ReadAllText(writer.CurrentFile);
        Assert.Contains("primera linea", content);
        Assert.Contains("segunda linea", content);
        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void SurvivesTwoWritersOnTheSameFile()
    {
        var first = new LogWriter(_folder);
        var second = new LogWriter(_folder);

        first.Write("uno");
        second.Write("dos");

        Assert.Contains("dos", File.ReadAllText(first.CurrentFile));
    }

    [Fact]
    public void DeletesLogsOlderThanTheRetentionWindow()
    {
        Directory.CreateDirectory(_folder);
        var old = Path.Combine(_folder, "agent-2026-08-01.log");
        var recent = Path.Combine(_folder, "agent-2026-09-12.log");
        File.WriteAllText(old, "viejo");
        File.WriteAllText(recent, "reciente");

        LogWriter.Prune(_folder, new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), keepDays: 30);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void PruningIgnoresFilesItDoesNotRecognise()
    {
        Directory.CreateDirectory(_folder);
        var other = Path.Combine(_folder, "notas.txt");
        File.WriteAllText(other, "algo");

        LogWriter.Prune(_folder, new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), keepDays: 1);

        Assert.True(File.Exists(other));
    }

    [Fact]
    public void TheFileIsUtf8WithAByteOrderMarkSoAccentsSurviveAnyEditor()
    {
        var writer = new LogWriter(_folder);

        writer.Write("relevamiento terminado sin más problemas");

        var bytes = File.ReadAllBytes(writer.CurrentFile);
        Assert.Equal([(byte)0xEF, (byte)0xBB, (byte)0xBF], bytes.Take(3));
        Assert.Contains("sin más problemas", File.ReadAllText(writer.CurrentFile));
    }

    [Fact]
    public void PruningAMissingFolderIsHarmless()
    {
        LogWriter.Prune(Path.Combine(_folder, "no-existe"), DateTimeOffset.Now, keepDays: 30);
    }
}
