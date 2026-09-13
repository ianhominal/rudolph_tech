using System.Diagnostics;
using RudolphTech.Core.Agent;
using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Talks to the real deployed application, so it only runs when somebody asks for it explicitly:
///
///   $env:RUDOLPH_SMOKE = "1"; $env:RUDOLPH_URL = "https://..."; $env:RUDOLPH_PASSWORD = "..."
///   dotnet test --filter FullyQualifiedName~RealEndpointSmoke
///
/// Without those variables it does nothing, which is how it behaves in CI. No password is ever
/// written into this file. It logs in, downloads and unpacks the package into a temporary folder,
/// and installs the dependency with the portable Node; it never starts a survey.
/// </summary>
public class RealEndpointSmoke
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RUDOLPH_SMOKE") == "1";

    [Fact]
    public async Task LoginDownloadAndPrepareTheAgentAgainstTheRealApp()
    {
        if (!Enabled) return;

        var url = AppUrl.Normalize(Environment.GetEnvironmentVariable("RUDOLPH_URL") ?? "https://rudolph-mvp.vercel.app");
        var password = Environment.GetEnvironmentVariable("RUDOLPH_PASSWORD") ?? "";
        Assert.False(string.IsNullOrEmpty(password), "Falta RUDOLPH_PASSWORD.");

        using var http = RudolphClient.CreateHttpClient();
        var client = new RudolphClient(http);

        var wrong = await client.LoginAsync(url, password + "-mal", CancellationToken.None);
        Assert.False(wrong.Success);
        Console.WriteLine($"Contraseña incorrecta rechazada: {wrong.Error}");

        var login = await client.LoginAsync(url, password, CancellationToken.None);
        Assert.True(login.Success, login.Error);
        Assert.StartsWith("rudolph_session=", login.SessionCookie!, StringComparison.Ordinal);
        Console.WriteLine("Sesión iniciada y cookie recibida.");

        var download = await client.DownloadPackageAsync(url, login.SessionCookie!, CancellationToken.None);
        Assert.True(download.Success, download.Error);
        Console.WriteLine($"Paquete descargado: {download.Content.Length} bytes.");

        var folder = Path.Combine(Path.GetTempPath(), "rudolph-tech-smoke", Guid.NewGuid().ToString("n"));
        try
        {
            var install = AgentPackageInstaller.Install(download.Content, folder);
            Assert.True(install.Success, install.Error);
            Assert.True(File.Exists(Path.Combine(folder, "meli-survey.mjs")));
            Assert.True(File.Exists(Path.Combine(folder, "meli-lib.mjs")));
            Assert.True(File.Exists(Path.Combine(folder, "package.json")));
            Assert.False(File.Exists(Path.Combine(folder, ".env")));
            Assert.Empty(Directory.GetFiles(folder, "*.bat"));
            Assert.False(string.IsNullOrEmpty(install.IngestToken));
            Console.WriteLine($"Paquete instalado: {install.FileCount} archivos, token presente, sin .env en disco.");
            Console.WriteLine($"RUDOLPH_APP_URL del paquete: {install.PackageAppUrl}");

            var node = FindBundledNode();
            Assert.NotNull(node);
            var exitCode = RunNpmInstall(node!, folder);
            Assert.Equal(0, exitCode);
            Assert.True(AgentPackageInstaller.DependenciesInstalled(folder));
            Console.WriteLine("npm install --omit=dev terminó y playwright-core quedó instalado.");

            // The generated .env is written and deleted around every run; check both halves here
            // without ever starting the survey itself.
            AgentEnvironment.WriteFile(folder, url, install.IngestToken, chromeOffScreen: true);
            var written = AgentEnvironment.Parse(File.ReadAllText(Path.Combine(folder, ".env")));
            Assert.Equal(install.IngestToken, written["SURVEY_INGEST_TOKEN"]);
            AgentPackageInstaller.RemoveEnvFile(folder);
            Assert.False(File.Exists(Path.Combine(folder, ".env")));
            Console.WriteLine("El .env se generó y se borró correctamente.");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static string? FindBundledNode()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && folder is not null; depth++, folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "build", "node", "node.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static int RunNpmInstall(string node, string folder)
    {
        var npm = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
        Assert.True(File.Exists(npm), $"No está npm-cli.js junto a {node}");

        var start = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(npm);
        foreach (var argument in Survey.SurveyCommand.NpmInstallArguments()) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        Console.WriteLine(process.StandardOutput.ReadToEnd());
        Console.WriteLine(process.StandardError.ReadToEnd());
        process.WaitForExit();
        return process.ExitCode;
    }
}
