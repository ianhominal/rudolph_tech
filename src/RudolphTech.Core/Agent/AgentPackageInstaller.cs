using System.IO.Compression;
using System.Text;

namespace RudolphTech.Core.Agent;

public sealed class AgentPackageInstallResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";

    /// <summary> SURVEY_INGEST_TOKEN read out of the package's .env; never written to disk by this class. </summary>
    public string IngestToken { get; init; } = "";

    /// <summary> RUDOLPH_APP_URL as the package itself declared it (the origin the download came from). </summary>
    public string PackageAppUrl { get; init; } = "";

    public int FileCount { get; init; }

    public static AgentPackageInstallResult Failed(string error) => new() { Error = error };
}

/// <summary>
/// Unpacks the zip GET /api/agente/descargar answers with (see web/src/app/api/agente/descargar and
/// web/src/lib/agent-package.ts) into %LocalAppData%\RudolphTech\agent.
///
/// Two deliberate differences from unzipping it by hand:
/// the .bat files and LEEME.txt of the old manual install are dropped, because the tray app is what
/// schedules and runs things now; and the .env, the only file in the package carrying a secret, is
/// read into memory and never written, so the token lives protected in settings.json instead of in
/// plain text in a folder anybody can open.
/// </summary>
public static class AgentPackageInstaller
{
    private const string SurveyScript = "meli-survey.mjs";

    /// <summary>
    /// Size caps on the download. The real package is a handful of small text files (well under one
    /// megabyte), so these are generous by a wide margin and still keep a zip bomb, or a mistake on
    /// the server, from filling the disk of the office PC. The archive is read into memory before
    /// anything is written, so the limits are enforced before a single file lands on disk.
    /// </summary>
    public const long MaxPackageBytes = 20L * 1024 * 1024;

    public const long MaxEntryBytes = 5L * 1024 * 1024;

    public const long MaxTotalBytes = 30L * 1024 * 1024;

    /// <summary> Thrown while reading the archive and turned into a message by <see cref="Install"/>. </summary>
    private sealed class PackageTooLargeException : Exception
    {
        public PackageTooLargeException(string message) : base(message) { }
    }

    public static AgentPackageInstallResult Install(byte[] package, string agentFolder)
    {
        if (package.LongLength > MaxPackageBytes)
        {
            return AgentPackageInstallResult.Failed(
                $"El paquete descargado es demasiado grande ({Megabytes(package.LongLength)} MB). No se instaló nada.");
        }

        Dictionary<string, byte[]> files;
        Dictionary<string, string> env;
        try
        {
            (files, env) = ReadPackage(package);
        }
        catch (InvalidDataException)
        {
            return AgentPackageInstallResult.Failed("El paquete descargado no es un archivo zip válido.");
        }
        catch (PackageTooLargeException exception)
        {
            return AgentPackageInstallResult.Failed(exception.Message);
        }

        if (!files.ContainsKey(SurveyScript))
        {
            return AgentPackageInstallResult.Failed($"El paquete descargado no trae {SurveyScript}.");
        }

        if (!env.TryGetValue(AgentEnvironment.IngestTokenKey, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AgentPackageInstallResult.Failed(
                $"El paquete descargado no trae {AgentEnvironment.IngestTokenKey}. Avisá a quien administra la aplicación.");
        }

        try
        {
            Directory.CreateDirectory(agentFolder);
            foreach (var (name, content) in files)
            {
                var destination = Path.GetFullPath(Path.Combine(agentFolder, name));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, content);
            }
            RemoveEnvFile(agentFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AgentPackageInstallResult.Failed($"No se pudo guardar el agente en {agentFolder}: {exception.Message}");
        }

        return new AgentPackageInstallResult
        {
            Success = true,
            IngestToken = token,
            PackageAppUrl = env.GetValueOrDefault(AgentEnvironment.AppUrlKey, ""),
            FileCount = files.Count,
        };
    }

    /// <summary>
    /// Everything is read and validated before a single byte is written, so a package that turns out
    /// to be incomplete never half replaces a working install.
    /// </summary>
    private static (Dictionary<string, byte[]> Files, Dictionary<string, string> Env) ReadPackage(byte[] package)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // a directory entry
            if (!IsInsideFolder(entry.FullName)) continue;
            if (IsIgnored(entry.Name)) continue;

            var content = ReadEntry(entry, ref total);

            if (string.Equals(entry.Name, AgentEnvironment.FileName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (key, value) in AgentEnvironment.Parse(Encoding.UTF8.GetString(content))) env[key] = value;
                continue;
            }

            files[entry.FullName] = content;
        }

        return (files, env);
    }

    /// <summary>
    /// Reads one entry with both caps enforced. The size declared in the archive is checked first
    /// because it is free, and then the bytes that actually come out are counted as they are read:
    /// a zip can claim whatever it likes about how big an entry is.
    /// </summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, ref long total)
    {
        if (entry.Length > MaxEntryBytes) throw new PackageTooLargeException(TooBig(entry.FullName, entry.Length));

        var remaining = Math.Min(MaxEntryBytes, MaxTotalBytes - total) + 1;
        using var content = new MemoryStream();
        using (var source = entry.Open())
        {
            var buffer = new byte[81920];
            int read;
            while (remaining > 0 && (read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
            {
                content.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        if (content.Length > MaxEntryBytes) throw new PackageTooLargeException(TooBig(entry.FullName, content.Length));

        total += content.Length;
        if (total > MaxTotalBytes)
        {
            throw new PackageTooLargeException(
                $"El paquete descargado ocupa demasiado al descomprimirse (más de {Megabytes(MaxTotalBytes)} MB). No se instaló nada.");
        }

        return content.ToArray();
    }

    private static string TooBig(string name, long size) =>
        $"El archivo {name} del paquete es demasiado grande ({Megabytes(size)} MB). No se instaló nada.";

    private static string Megabytes(long bytes) => (bytes / (1024d * 1024d)).ToString("0.#");

    /// <summary> The batch files and the readme of the old Scheduled Task install have no use here. </summary>
    private static bool IsIgnored(string name) =>
        name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "LEEME.txt", StringComparison.OrdinalIgnoreCase);

    /// <summary> Refuses "../" style entries: the package comes from our own app, but a zip is still a zip. </summary>
    private static bool IsInsideFolder(string entryPath) =>
        !Path.IsPathRooted(entryPath)
        && !entryPath.Split('/', '\\').Any(segment => segment == "..");

    /// <summary>
    /// Deletes the .env next to the scripts. Called after every run and at startup, so a run that
    /// was killed halfway never leaves the ingest token sitting in a readable file.
    /// </summary>
    public static void RemoveEnvFile(string agentFolder)
    {
        try
        {
            var path = Path.Combine(agentFolder, AgentEnvironment.FileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do about it here; the next run overwrites the file anyway.
        }
    }

    /// <summary> Whether the agent folder already holds a usable copy of the scripts. </summary>
    public static bool IsInstalled(string agentFolder) => File.Exists(Path.Combine(agentFolder, SurveyScript));

    /// <summary> Whether npm already installed the one dependency the survey needs (playwright-core). </summary>
    public static bool DependenciesInstalled(string agentFolder) =>
        File.Exists(Path.Combine(agentFolder, "node_modules", "playwright-core", "package.json"));
}
