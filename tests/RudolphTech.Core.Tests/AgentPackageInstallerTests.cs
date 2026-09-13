using System.IO.Compression;
using System.Text;
using RudolphTech.Core.Agent;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Unpacking the zip GET /api/agente/descargar answers with. The secret bearing .env is read into
/// memory and never left on disk, and the .bat / LEEME.txt files of the old manual install are dropped.
/// </summary>
public class AgentPackageInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rudolph-tech-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static byte[] RealisticZip() => BuildZip(
        ("meli-survey.mjs", "// survey"),
        ("meli-lib.mjs", "// lib"),
        ("package.json", """{ "name": "rudolph-agente-relevamiento", "type": "module" }"""),
        ("instalar.bat", "@echo off"),
        ("preparar.bat", "@echo off"),
        ("relevar-ahora.bat", "@echo off"),
        ("desinstalar.bat", "@echo off"),
        ("LEEME.txt", "instrucciones"),
        (".env", "RUDOLPH_APP_URL=https://x.test\nSURVEY_INGEST_TOKEN=secreto\nSURVEY_CHROME_MINIMIZED=\n"));

    [Fact]
    public void KeepsTheScriptsAndThePackageManifest()
    {
        var result = AgentPackageInstaller.Install(RealisticZip(), _folder);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_folder, "meli-survey.mjs")));
        Assert.True(File.Exists(Path.Combine(_folder, "meli-lib.mjs")));
        Assert.True(File.Exists(Path.Combine(_folder, "package.json")));
    }

    [Fact]
    public void DropsTheBatchFilesAndTheReadmeOfTheOldManualInstall()
    {
        AgentPackageInstaller.Install(RealisticZip(), _folder);

        Assert.Empty(Directory.GetFiles(_folder, "*.bat"));
        Assert.False(File.Exists(Path.Combine(_folder, "LEEME.txt")));
    }

    [Fact]
    public void NeverLeavesTheSecretsOnDisk()
    {
        var result = AgentPackageInstaller.Install(RealisticZip(), _folder);

        Assert.False(File.Exists(Path.Combine(_folder, ".env")));
        Assert.Equal("secreto", result.IngestToken);
        Assert.Equal("https://x.test", result.PackageAppUrl);
    }

    [Fact]
    public void RefusesAPackageWithoutTheSurveyScript()
    {
        var result = AgentPackageInstaller.Install(BuildZip(("LEEME.txt", "nada util")), _folder);

        Assert.False(result.Success);
        Assert.Contains("meli-survey.mjs", result.Error);
    }

    [Fact]
    public void RefusesAPackageWithoutTheToken()
    {
        var zip = BuildZip(("meli-survey.mjs", "// survey"), ("meli-lib.mjs", "// lib"), ("package.json", "{}"));

        var result = AgentPackageInstaller.Install(zip, _folder);

        Assert.False(result.Success);
        Assert.Contains("SURVEY_INGEST_TOKEN", result.Error);
    }

    [Fact]
    public void RefusesSomethingThatIsNotAZip()
    {
        var result = AgentPackageInstaller.Install(Encoding.UTF8.GetBytes("<html>error</html>"), _folder);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void IgnoresEntriesThatTryToEscapeTheFolder()
    {
        var zip = BuildZip(
            ("meli-survey.mjs", "// survey"),
            ("meli-lib.mjs", "// lib"),
            ("package.json", "{}"),
            (".env", "SURVEY_INGEST_TOKEN=secreto\n"),
            ("../escapado.txt", "no"));

        var result = AgentPackageInstaller.Install(zip, _folder);

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(_folder, "..", "escapado.txt")));
    }

    [Fact]
    public void ReplacesTheScriptsOfAPreviousDownloadAndKeepsNodeModules()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "node_modules", "playwright-core"));
        File.WriteAllText(Path.Combine(_folder, "node_modules", "playwright-core", "package.json"), "{}");
        File.WriteAllText(Path.Combine(_folder, "meli-survey.mjs"), "// version vieja");

        var result = AgentPackageInstaller.Install(RealisticZip(), _folder);

        Assert.True(result.Success);
        Assert.Equal("// survey", File.ReadAllText(Path.Combine(_folder, "meli-survey.mjs")));
        Assert.True(File.Exists(Path.Combine(_folder, "node_modules", "playwright-core", "package.json")));
    }

    [Fact]
    public void RemovesAnEnvFileLeftBehindByAnInterruptedRun()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, ".env"), "SURVEY_INGEST_TOKEN=viejo");

        AgentPackageInstaller.RemoveEnvFile(_folder);

        Assert.False(File.Exists(Path.Combine(_folder, ".env")));
    }

    [Fact]
    public void RemovingAMissingEnvFileIsHarmless()
    {
        Directory.CreateDirectory(_folder);

        AgentPackageInstaller.RemoveEnvFile(_folder);
    }
}
