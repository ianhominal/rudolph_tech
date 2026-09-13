using System.IO.Compression;
using System.Text;
using RudolphTech.Core.Agent;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Size caps on the downloaded package. The zip comes from our own application over https, but it
/// is still an archive being unpacked into the person's profile, and a zip bomb is cheap to build:
/// nothing here is allowed to fill the disk of the office PC.
/// </summary>
public class AgentPackageSizeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rudolph-tech-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary> A package that is fine except for the entries the test adds on top. </summary>
    private static byte[] BuildZip(CompressionLevel level, params (string Name, byte[] Content)[] extras)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                using var stream = zip.CreateEntry(name, level).Open();
                stream.Write(content);
            }

            Add("meli-survey.mjs", Encoding.UTF8.GetBytes("// survey"));
            Add("meli-lib.mjs", Encoding.UTF8.GetBytes("// lib"));
            Add("package.json", Encoding.UTF8.GetBytes("{}"));
            Add(".env", Encoding.UTF8.GetBytes("SURVEY_INGEST_TOKEN=secreto\n"));
            foreach (var (name, content) in extras) Add(name, content);
        }
        return buffer.ToArray();
    }

    [Fact]
    public void AHealthyPackageIsWellUnderEveryLimit()
    {
        var result = AgentPackageInstaller.Install(BuildZip(CompressionLevel.Optimal), _folder);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void RefusesADownloadBiggerThanTwentyMegabytes()
    {
        // Random bytes stored without compression, so the zip on the wire really is that big.
        var noise = new byte[21 * 1024 * 1024];
        Random.Shared.NextBytes(noise);
        var package = BuildZip(CompressionLevel.NoCompression, ("relleno.bin", noise));
        Assert.True(package.Length > AgentPackageInstaller.MaxPackageBytes);

        var result = AgentPackageInstaller.Install(package, _folder);

        Assert.False(result.Success);
        Assert.Contains("demasiado grande", result.Error);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void RefusesASingleFileBiggerThanFiveMegabytes()
    {
        // Six megabytes of zeros compress to almost nothing: small download, big file on disk.
        var package = BuildZip(CompressionLevel.Optimal, ("gordo.bin", new byte[6 * 1024 * 1024]));
        Assert.True(package.Length < AgentPackageInstaller.MaxPackageBytes);

        var result = AgentPackageInstaller.Install(package, _folder);

        Assert.False(result.Success);
        Assert.Contains("gordo.bin", result.Error);
        Assert.Contains("demasiado grande", result.Error);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void RefusesAPackageThatAddsUpToMoreThanThirtyMegabytes()
    {
        var extras = Enumerable.Range(0, 8)
            .Select(index => ($"parte-{index}.bin", new byte[4 * 1024 * 1024]))
            .ToArray();
        var package = BuildZip(CompressionLevel.Optimal, extras);

        var result = AgentPackageInstaller.Install(package, _folder);

        Assert.False(result.Success);
        Assert.Contains("ocupa demasiado", result.Error);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void AnEntryThatLiesAboutItsSizeIsStoppedWhileItIsBeingRead()
    {
        // The size written in the zip header is not evidence of anything, so the limit is also
        // enforced over the bytes that actually come out of the entry.
        var package = BuildZip(CompressionLevel.Optimal, ("mentiroso.bin", new byte[6 * 1024 * 1024]));
        var tampered = ClearDeclaredSizes(package);

        var result = AgentPackageInstaller.Install(tampered, _folder);

        Assert.False(result.Success);
        Assert.Contains("demasiado grande", result.Error);
    }

    /// <summary>
    /// Rewrites every local file header's uncompressed size to zero, the way a hand made zip could
    /// claim that a huge entry is empty. The central directory is left alone so the archive still
    /// opens.
    /// </summary>
    private static byte[] ClearDeclaredSizes(byte[] package)
    {
        var copy = (byte[])package.Clone();
        for (var i = 0; i + 30 < copy.Length; i++)
        {
            var isLocalHeader = copy[i] == 0x50 && copy[i + 1] == 0x4B && copy[i + 2] == 0x03 && copy[i + 3] == 0x04;
            if (!isLocalHeader) continue;
            for (var offset = 22; offset < 26; offset++) copy[i + offset] = 0; // uncompressed size
        }
        return copy;
    }
}
