namespace RudolphTech.Core.Agent;

/// <summary> Either the node.exe that ships with the app, or a reason to show the person. </summary>
public sealed class NodeLocation
{
    public bool Found => Path is not null;
    public string? Path { get; init; }
    public string Error { get; init; } = "";
}

/// <summary>
/// Finds the Node the survey runs on. Only the copy that travels with the application counts:
/// node\node.exe inside the install folder (how the installer lays it out) or build\node\node.exe
/// walking up from wherever the executable is running (how tools/get-node.ps1 leaves it during
/// development).
///
/// There is deliberately no fallback to whatever node.exe happens to be on PATH. Bundling a pinned
/// runtime exists precisely so the office PC does not depend on some other Node of some other
/// version that somebody installed for something else, and picking one up silently would hide a
/// broken install instead of showing it. When neither copy is there, this fails closed and the
/// message reaches the settings window and the tray balloon.
/// </summary>
public static class NodeLocator
{
    public const string NotFoundMessage = "No se encontró el Node incluido. Reinstalá Rudolph Tech.";

    /// <summary>
    /// How many folders up from the executable the development copy is looked for. Eight steps
    /// are enough to reach the repository root from the publish folder during development.
    /// </summary>
    public const int DevelopmentSearchDepth = 8;

    /// <summary> Pure: the paths to try, in order, for an application running from <paramref name="baseDirectory"/>. </summary>
    public static IReadOnlyList<string> Candidates(string baseDirectory, int depth = DevelopmentSearchDepth)
    {
        var candidates = new List<string> { Path.Combine(baseDirectory, "node", "node.exe") };

        var folder = baseDirectory;
        for (var step = 0; step <= depth && !string.IsNullOrEmpty(folder); step++)
        {
            candidates.Add(Path.Combine(folder, "build", "node", "node.exe"));
            folder = Path.GetDirectoryName(folder);
        }

        return candidates;
    }

    /// <summary> Pure: the first candidate that <paramref name="exists"/> accepts, or a failure. </summary>
    public static NodeLocation Locate(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        foreach (var candidate in candidates)
        {
            if (exists(candidate)) return new NodeLocation { Path = candidate };
        }

        return new NodeLocation { Error = NotFoundMessage };
    }

    /// <summary>
    /// npm ships with Node as a plain script; running it as "node npm-cli.js" avoids depending on
    /// npm.cmd, which would need a shell and would flash a console window.
    /// </summary>
    public static string NpmCliPathFor(string nodeExecutable) =>
        Path.Combine(Path.GetDirectoryName(nodeExecutable) ?? "", "node_modules", "npm", "bin", "npm-cli.js");
}
