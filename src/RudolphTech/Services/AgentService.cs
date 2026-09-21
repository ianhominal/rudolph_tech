using RudolphTech.Core.Agent;
using RudolphTech.Core.Logging;
using RudolphTech.Core.Scheduling;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;
using RudolphTech.Core.Web;

namespace RudolphTech.Services;

/// <summary>
/// Everything that actually happens: logging in, downloading and unpacking the agent package,
/// installing its one dependency, and running the survey. The tray and the settings window only
/// call into this and listen to its events.
///
/// The session cookie lives in memory and nowhere else. It is obtained whenever somebody types the
/// password (opening the settings window is exactly that) and lost when the app closes. A daily
/// package refresh that finds no session simply logs it and lets the survey run with the scripts
/// already on disk, which keep working because the ingest token is stored protected.
/// </summary>
/// <summary> What came of telling the web app this program is open. "Not linked" is not a failure: there is nothing to tell it with yet. </summary>
public enum HeartbeatOutcome
{
    NotLinked,
    Sent,
    Failed,
}

public sealed class AgentService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly LogWriter _log;
    private readonly RudolphClient _client;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly SemaphoreSlim _heartbeatGate = new(1, 1);

    private string? _sessionCookie;
    private CancellationTokenSource? _running;
    private DateOnly? _refreshFailureReported;

    public AgentService(SettingsStore store, AppSettings settings, LogWriter log)
    {
        _store = store;
        _log = log;
        Settings = settings;
        _http = RudolphClient.CreateHttpClient();
        _client = new RudolphClient(_http);
    }

    public AppSettings Settings { get; }

    public LogWriter Log => _log;

    public bool IsRunning => _running is not null;

    public bool HasSession => _sessionCookie is not null;

    /// <summary> Raised when a run starts or ends, always off the UI thread. </summary>
    public event Action<bool>? RunningChanged;

    /// <summary> Raised with the finished run so the tray can show a balloon and the window can refresh. </summary>
    public event Action<RunOutcome>? RunFinished;

    public void SaveSettings() => _store.Save(Settings);

    public async Task<LoginResult> LoginAsync(string appUrl, string password, CancellationToken cancellation)
    {
        var result = await _client.LoginAsync(appUrl, password, cancellation);
        if (result.Success)
        {
            _sessionCookie = result.SessionCookie;
            _log.Write($"Sesión iniciada en {appUrl}.");
        }
        else
        {
            _log.Write($"No se pudo iniciar sesión en {appUrl}: {result.Error}");
        }
        return result;
    }

    public void ForgetSession() => _sessionCookie = null;

    /// <summary> Everything the schedule depends on, read at this instant. Shared so the tray's own tick and the heartbeat never disagree about it. </summary>
    public ScheduleInputs CurrentScheduleInputs(DateTimeOffset now) => new()
    {
        Now = now,
        PendingIntervalMinutes = Settings.PendingIntervalMinutes,
        DailyTime = Settings.DailyTime,
        Paused = Settings.Paused,
        Configured = Settings.IsConfigured,
        IsRunning = IsRunning,
        LastPendingRun = Settings.LastPendingRun,
        LastDailyRun = Settings.LastDailyRun,
        DailyEnabled = Settings.DailyEnabled,
        PendingEnabled = Settings.PendingEnabled,
        BlockedUntil = Settings.BlockedUntil,
    };

    /// <summary>
    /// Tells the web app this program is open and when its next survey is due, so the Competencia screen
    /// can say "proximo relevamiento en 4 min" instead of leaving a requested survey looking stuck with
    /// no way to tell a wait from a closed program.
    ///
    /// Reads the state <em>after</em> taking the gate, and never sends two at once. Both matter: starting
    /// a run raises RunningChanged while the tick's own heartbeat is still in flight, and two unordered
    /// posts could leave the web app holding "no está relevando" over a survey that is running.
    /// </summary>
    public async Task<HeartbeatOutcome> SendHeartbeatAsync(CancellationToken cancellation)
    {
        if (!Settings.IsConfigured || Settings.IngestToken is not { } token) return HeartbeatOutcome.NotLinked;

        await _heartbeatGate.WaitAsync(cancellation);
        try
        {
            var now = DateTimeOffset.Now;
            var heartbeat = Heartbeat.For(
                ScheduleDecider.NextRunAt(CurrentScheduleInputs(now)),
                now,
                Settings.Paused,
                IsRunning,
                Settings.PendingEnabled);

            return await _client.PostHeartbeatAsync(Settings.AppUrl, token, heartbeat, cancellation)
                ? HeartbeatOutcome.Sent
                : HeartbeatOutcome.Failed;
        }
        finally
        {
            _heartbeatGate.Release();
        }
    }

    /// <summary>
    /// Downloads the package again and unpacks it, keeping the token out of any file. Needs a
    /// session, which only typing the password can produce.
    /// </summary>
    public async Task<(bool Success, string Message)> RefreshPackageAsync(CancellationToken cancellation)
    {
        if (_sessionCookie is null)
        {
            return (false, "Hace falta iniciar sesión para actualizar el agente.");
        }

        var download = await _client.DownloadPackageAsync(Settings.AppUrl, _sessionCookie, cancellation);
        if (!download.Success)
        {
            // An expired access is not a session problem: the cookie is still good and throwing it
            // away would only make the next attempt ask for the password for nothing.
            if (!download.Expired && download.Error.Contains("sesión", StringComparison.OrdinalIgnoreCase)) _sessionCookie = null;
            _log.Write($"No se pudo descargar el agente: {download.Error}");
            return (false, download.Error);
        }

        var install = AgentPackageInstaller.Install(download.Content, AppPaths.AgentFolder);
        if (!install.Success)
        {
            _log.Write($"No se pudo instalar el agente: {install.Error}");
            return (false, install.Error);
        }

        Settings.IngestToken = install.IngestToken;
        Settings.PackageAppUrl = install.PackageAppUrl;
        Settings.LastPackageDownload = DateTimeOffset.Now;
        SaveSettings();
        _log.Write($"Agente actualizado: {install.FileCount} archivos.");

        var dependencies = await EnsureDependenciesAsync(cancellation);
        return dependencies.Success
            ? (true, "El agente quedó actualizado.")
            : (false, dependencies.Message);
    }

    /// <summary> Runs npm install inside the agent folder, only when playwright-core is not there yet. </summary>
    public async Task<(bool Success, string Message)> EnsureDependenciesAsync(CancellationToken cancellation)
    {
        if (AgentPackageInstaller.DependenciesInstalled(AppPaths.AgentFolder)) return (true, "");

        var node = NodeRuntime.Locate();
        if (!node.Found)
        {
            _log.Write(node.Error);
            return (false, node.Error);
        }

        var npm = NodeRuntime.FindNpmCli(node.Path!);
        if (npm is null) return (false, "No se encontró npm junto al Node incluido. Reinstalá Rudolph Tech.");

        _log.Write("Instalando las dependencias del agente (playwright-core).");
        var runner = new ProcessRunner(_log.WriteScriptOutput);
        var arguments = new List<string> { npm };
        arguments.AddRange(SurveyCommand.NpmInstallArguments());

        try
        {
            var exitCode = await runner.RunAsync(
                node.Path!,
                arguments,
                AppPaths.AgentFolder,
                new Dictionary<string, string>(StringComparer.Ordinal),
                cancellation);

            if (exitCode != 0) return (false, $"La instalación de dependencias falló (código {exitCode}).");
            _log.Write("Dependencias instaladas.");
            return (true, "");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write($"La instalación de dependencias falló: {exception.Message}");
            return (false, "La instalación de dependencias falló. Mirá el registro.");
        }
    }

    /// <summary>
    /// Runs the survey once. Never overlaps: a second call while one is running simply returns null.
    /// </summary>
    public async Task<RunOutcome?> RunSurveyAsync(SurveyRunKind kind, CancellationToken cancellation)
    {
        if (!await _runGate.WaitAsync(0, cancellation)) return null;

        var started = DateTimeOffset.Now;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _running = linked;
        RunningChanged?.Invoke(true);

        try
        {
            var (outcome, stateIsFromThisRun) = await RunSurveyCoreAsync(kind, started, linked.Token);
            Remember(kind, started, outcome, stateIsFromThisRun);
            RunFinished?.Invoke(outcome);
            return outcome;
        }
        finally
        {
            AgentPackageInstaller.RemoveEnvFile(AppPaths.AgentFolder);
            _running = null;
            RunningChanged?.Invoke(false);
            _runGate.Release();
        }
    }

    /// <summary>
    /// Runs the script and turns its exit code plus progress file into an outcome. The second value
    /// says whether that progress file was actually written by this run, not left over from an
    /// earlier one: the script can exit 0 without ever touching it, on the "--pending, nothing due"
    /// path, so a leftover file must not be read as fresh evidence of anything (H1). Every early
    /// return here skips the script entirely, so none of them read a state file at all; false is the
    /// honest answer for all of them, not just a placeholder.
    /// </summary>
    private async Task<(RunOutcome Outcome, bool StateIsFromThisRun)> RunSurveyCoreAsync(SurveyRunKind kind, DateTimeOffset started, CancellationToken cancellation)
    {
        if (!Settings.IsConfigured || Settings.IngestToken is not { } token)
        {
            return (Failure("Falta configurar la aplicación. Abrí la configuración e iniciá sesión."), false);
        }

        if (!AgentPackageInstaller.IsInstalled(AppPaths.AgentFolder))
        {
            return (Failure("El agente no está descargado. Abrí la configuración y tocá Actualizar agente."), false);
        }

        var dependencies = await EnsureDependenciesAsync(cancellation);
        if (!dependencies.Success) return (Failure(dependencies.Message), false);

        var node = NodeRuntime.Locate();
        if (!node.Found) return (Failure(node.Error), false);

        // Both at once: real environment variables (which meli-lib.mjs layers on top of any file, so
        // they always win) and a .env written for this run only, which is what the script would read
        // if it were ever launched by hand from that folder. The file is deleted right afterwards.
        var environment = AgentEnvironment.BuildProcessVariables(Settings.AppUrl, token, Settings.ChromeOffScreen);
        AgentEnvironment.WriteFile(AppPaths.AgentFolder, Settings.AppUrl, token, Settings.ChromeOffScreen);

        _log.Write($"Arranca el relevamiento ({Describe(kind)}).");
        var runner = new ProcessRunner(_log.WriteScriptOutput);

        int exitCode;
        try
        {
            exitCode = await runner.RunAsync(
                node.Path!,
                SurveyCommand.BuildArguments(kind, AppPaths.SurveyScript),
                AppPaths.AgentFolder,
                environment,
                cancellation);
        }
        catch (OperationCanceledException)
        {
            _log.Write("El relevamiento se detuvo por pedido del usuario.");
            return (RunOutcome.From(kind, 1, new SurveyState { Status = SurveyStatus.Interrupted }), false);
        }
        catch (Exception exception)
        {
            _log.Write($"El relevamiento no pudo arrancar: {exception.Message}");
            return (Failure($"El relevamiento no pudo arrancar: {exception.Message}"), false);
        }

        var state = SurveyStateFile.Read(AppPaths.StateFile);
        var outcome = RunOutcome.From(kind, exitCode, state);

        // started is captured before the process is even launched, so a state file this run actually
        // wrote always carries a startedAt at or after it; a file left over from an earlier run is
        // always strictly before it. null (no file, or one the script never touched this time) is not
        // fresh either: there is nothing to prove this run wrote anything.
        var stateIsFromThisRun = state?.StartedAt is { } stateStarted && stateStarted >= started;

        _log.Write($"Fin del relevamiento (código {exitCode}): {outcome.Message}");
        return (outcome, stateIsFromThisRun);
    }

    private void Remember(SurveyRunKind kind, DateTimeOffset started, RunOutcome outcome, bool stateIsFromThisRun)
    {
        var finished = DateTimeOffset.Now;
        Settings.LastRun = RunSummary.From(kind, started, finished, outcome);
        if (kind == SurveyRunKind.Pending) Settings.LastPendingRun = finished;
        if (kind == SurveyRunKind.Daily) Settings.LastDailyRun = DateOnly.FromDateTime(started.Date);

        // A manual run also counts as the pending check for the interval, so pressing the button
        // does not immediately queue another one behind it.
        if (kind == SurveyRunKind.Manual) Settings.LastPendingRun = finished;

        // Real backoff: a wall holds automatic runs for hours instead of the next 5-15 minute tick
        // (BB-2..3). BlockBackoff.Next only looks at the outcome and at whether the state behind it is
        // fresh, never at kind: a walled manual run ("Relevar ahora") escalates the automatic hold
        // exactly like a walled automatic one would. Deliberate (BB-2's contract has no "except manual"
        // qualifier): a wall is real evidence Mercado Libre is challenging this PC, whoever triggered
        // the run that hit it. "Relevar ahora" itself still needs no check against an existing hold,
        // because it never consults ScheduleDecider in the first place (see
        // TrayApplicationContext.StartManualRun): a person pressing the button is a person who can
        // answer the wall.
        var current = new BlockBackoff.Hold(Settings.BlockedUntil, Settings.BlockedStreak, Settings.BlockedStreakDay);
        var hold = BlockBackoff.Next(current, outcome.Kind, stateIsFromThisRun, finished, Settings.DailyTime);
        (Settings.BlockedUntil, Settings.BlockedStreak, Settings.BlockedStreakDay) = (hold.Until, hold.Streak, hold.Day);

        SaveSettings();
    }

    /// <summary>
    /// Called before the daily survey: keeps the scripts in step with whatever the web app deployed.
    /// Failing is not fatal, and it complains at most once a day.
    /// </summary>
    public async Task RefreshPackageIfDueAsync(CancellationToken cancellation)
    {
        var now = DateTimeOffset.Now;
        if (!PackageRefreshPolicy.NeedsRefresh(Settings.LastPackageDownload, now, AgentPackageInstaller.IsInstalled(AppPaths.AgentFolder)))
        {
            return;
        }

        if (_sessionCookie is null)
        {
            var today = DateOnly.FromDateTime(now.Date);
            if (_refreshFailureReported != today)
            {
                _refreshFailureReported = today;
                _log.Write("No se pudo actualizar el agente: no hay sesión iniciada. Se sigue usando la copia actual.");
            }
            return;
        }

        await RefreshPackageAsync(cancellation);
    }

    /// <summary> Asks a running survey to stop, taking Chrome down with it. </summary>
    public void StopRunningSurvey() => _running?.Cancel();

    private static string Describe(SurveyRunKind kind) => kind switch
    {
        SurveyRunKind.Pending => "pedidos pendientes",
        SurveyRunKind.Daily => "diario",
        _ => "manual",
    };

    private RunOutcome Failure(string message)
    {
        _log.Write(message);
        return RunOutcome.From(SurveyRunKind.Manual, 1, new SurveyState { Status = SurveyStatus.Error, Message = message });
    }

    public void Dispose()
    {
        _running?.Cancel();
        _runGate.Dispose();
        _heartbeatGate.Dispose();
        _http.Dispose();
    }
}
