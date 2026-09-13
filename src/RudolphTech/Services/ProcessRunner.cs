using System.Diagnostics;

namespace RudolphTech.Services;

/// <summary>
/// Starts a child process (node.exe, or node.exe running npm's own cli), streams both of its output
/// channels into the log, and waits for it to end. No console window is ever shown: the only window
/// the survey needs is the Chrome one, and the script opens that by itself.
/// </summary>
public sealed class ProcessRunner
{
    private readonly Action<string> _onOutput;

    public ProcessRunner(Action<string> onOutput) => _onOutput = onOutput;

    /// <summary> The process id of the child while it runs, so a survey can be stopped on exit. </summary>
    public int? CurrentProcessId { get; private set; }

    public async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellation)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (key, value) in environment) start.Environment[key] = value;

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Report(e.Data);
        process.ErrorDataReceived += (_, e) => Report(e.Data);

        process.Start();
        CurrentProcessId = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
        finally
        {
            CurrentProcessId = null;
        }

        return process.ExitCode;
    }

    private void Report(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) _onOutput(line);
    }

    /// <summary> The survey starts Chrome as a child of node, so stopping it has to take the whole tree. </summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It ended on its own between the check and the call.
        }
    }
}
