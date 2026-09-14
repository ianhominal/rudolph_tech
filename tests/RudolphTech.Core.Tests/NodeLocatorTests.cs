using RudolphTech.Core.Agent;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Which node.exe the survey runs on. Only the copy that ships with the app counts: a Node found on
/// PATH would be whatever happens to be installed on that PC, at whatever version, which is exactly
/// what bundling a runtime was meant to avoid.
/// </summary>
public class NodeLocatorTests
{
    private const string InstallFolder = @"C:\Program Files\Rudolph Tech";
    private static readonly string Bundled = Path.Combine(InstallFolder, "node", "node.exe");

    private static Func<string, bool> Exists(params string[] present) =>
        path => present.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TakesTheNodeInstalledNextToTheApplication()
    {
        var location = NodeLocator.Locate(NodeLocator.Candidates(InstallFolder), Exists(Bundled));

        Assert.True(location.Found);
        Assert.Equal(Bundled, location.Path);
        Assert.Equal("", location.Error);
    }

    [Fact]
    public void FallsBackToTheOneLeftByGetNodeDuringDevelopment()
    {
        // bin\Release\net10.0-windows\win-x64 walking up to the repository root, where build\node is.
        var repository = @"D:\Repo\rudolph_tech";
        var runningFrom = Path.Combine(repository, "src", "RudolphTech", "bin", "Release");
        var development = Path.Combine(repository, "build", "node", "node.exe");

        var location = NodeLocator.Locate(NodeLocator.Candidates(runningFrom), Exists(development));

        Assert.True(location.Found);
        Assert.Equal(development, location.Path);
    }

    [Fact]
    public void PrefersTheBundledCopyOverTheDevelopmentOne()
    {
        var development = Path.Combine(InstallFolder, "build", "node", "node.exe");

        var location = NodeLocator.Locate(NodeLocator.Candidates(InstallFolder), Exists(Bundled, development));

        Assert.Equal(Bundled, location.Path);
    }

    [Fact]
    public void WithoutTheBundledNodeItFailsInsteadOfLookingAroundThePc()
    {
        var location = NodeLocator.Locate(NodeLocator.Candidates(InstallFolder), Exists());

        Assert.False(location.Found);
        Assert.Null(location.Path);
        Assert.Equal("No se encontró el Node incluido. Reinstalá Rudolph Tech.", location.Error);
    }

    [Fact]
    public void NeverConsidersAPlainNodeExeFromThePath()
    {
        var onPath = @"C:\Program Files\nodejs\node.exe";

        var location = NodeLocator.Locate(NodeLocator.Candidates(InstallFolder), Exists(onPath));

        Assert.False(location.Found);
    }

    [Fact]
    public void TheFirstCandidateIsAlwaysTheBundledOne()
    {
        Assert.Equal(Bundled, NodeLocator.Candidates(InstallFolder).First());
    }

    [Fact]
    public void TheDevelopmentSearchWalksUpButNotForever()
    {
        var candidates = NodeLocator.Candidates(@"C:\a\b\c\d\e\f\g\h\i\j");

        Assert.All(candidates, candidate => Assert.EndsWith("node.exe", candidate, StringComparison.Ordinal));
        Assert.InRange(candidates.Count, 2, 12);
    }

    [Fact]
    public void AcceptsTheCopyUnderLocalAppDataWhenNothingElseIsThere()
    {
        var localAppData = @"C:\Users\alguien\AppData\Local\RudolphTech\node\node.exe";

        var location = NodeLocator.Locate(
            NodeLocator.Candidates(InstallFolder, localAppData),
            Exists(localAppData));

        Assert.True(location.Found);
        Assert.Equal(localAppData, location.Path);
    }

    [Fact]
    public void PrefersTheBundledCopyOverTheOneUnderLocalAppData()
    {
        var localAppData = @"C:\Users\alguien\AppData\Local\RudolphTech\node\node.exe";

        var location = NodeLocator.Locate(
            NodeLocator.Candidates(InstallFolder, localAppData),
            Exists(Bundled, localAppData));

        Assert.Equal(Bundled, location.Path);
    }

    [Fact]
    public void WithNoLocalAppDataCandidateGivenItIsNeverAddedToTheList()
    {
        var candidates = NodeLocator.Candidates(InstallFolder);

        Assert.DoesNotContain(candidates, candidate => candidate.Contains("AppData", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StillFailsClosedWhenNeitherTheBundledNorTheLocalAppDataCopyIsThere()
    {
        var localAppData = @"C:\Users\alguien\AppData\Local\RudolphTech\node\node.exe";

        var location = NodeLocator.Locate(NodeLocator.Candidates(InstallFolder, localAppData), Exists());

        Assert.False(location.Found);
        Assert.Equal("No se encontró el Node incluido. Reinstalá Rudolph Tech.", location.Error);
    }

    [Fact]
    public void NpmTravelsWithTheNodeThatWasFound()
    {
        Assert.Equal(
            Path.Combine(InstallFolder, "node", "node_modules", "npm", "bin", "npm-cli.js"),
            NodeLocator.NpmCliPathFor(Bundled));
    }
}
