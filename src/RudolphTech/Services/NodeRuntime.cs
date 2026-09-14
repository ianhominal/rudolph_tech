using RudolphTech.Core.Agent;
using RudolphTech.Core.Settings;

namespace RudolphTech.Services;

/// <summary>
/// The thin filesystem shell over <see cref="NodeLocator"/>, which holds the decision itself and is
/// covered by tests. Nothing here looks at PATH: the only Node this app runs is the one that came
/// with it, and when that copy is missing the run fails with a message the person can act on.
/// </summary>
public static class NodeRuntime
{
    private static string LocalAppDataNodeExecutable => Path.Combine(AppPaths.Root, "node", "node.exe");

    public static NodeLocation Locate() =>
        NodeLocator.Locate(NodeLocator.Candidates(AppContext.BaseDirectory, LocalAppDataNodeExecutable), File.Exists);

    /// <summary> The npm that travels with that same Node, or null when it is not there. </summary>
    public static string? FindNpmCli(string nodeExecutable)
    {
        var cli = NodeLocator.NpmCliPathFor(nodeExecutable);
        return File.Exists(cli) ? cli : null;
    }
}
