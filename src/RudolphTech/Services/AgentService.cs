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

    /// <summary>
    /// Everything the schedule depends on, read at this instant. Shared so the tray's own tick and the
    /// heartbeat never disagree about it.
    ///
    /// blockedUntilOverride exists only for ResumesAt's preview below: it asks what the next automatic
    /// run would be if the hold about to be persisted were already in effect, before it actually is
    /// (H-1). Every other caller omits it and gets the real, currently persisted hold.
    /// </summary>
    public ScheduleInputs CurrentScheduleInputs(DateTimeOffset now, DateTimeOffset? blockedUntilOverride = null) => new()
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
        BlockedUntil = blockedUntilOverride ?? Settings.BlockedUntil,
    };

    /// <summary>
    /// The one number every surface naming a resume time must show: the tray tooltip, the
    /// Pausar/Reanudar balloon (both via TrayApplicationContext) and a fresh Blocked balloon's resume
    /// clause (via ResumesAt below) all call this, and it is exactly what SendHeartbeatAsync already
    /// sends the web, so none of the four can ever disagree about when something automatic happens
    /// next (H-1: BB-4 makes the daily survey immune to a hold, so the raw Settings.BlockedUntil alone
    /// can name a time later than what will really happen).
    /// </summary>
    public DateTimeOffset? NextRunAt(DateTimeOffset now) => ScheduleDecider.NextRunAt(CurrentScheduleInputs(now));

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
                NextRunAt(now),
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
            var (outcome, stateIsFromThisRun, finished) = await RunSurveyCoreAsync(kind, started, linked.Token);
            Remember(kind, started, finished, outcome, stateIsFromThisRun);
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
    /// honest answer for all of them, not just a placeholder. The third value is the instant Remember
    /// should treat as "finished"; for these early returns nothing actually ran, so the moment of the
    /// return itself is that instant.
    /// </summary>
    private async Task<(RunOutcome Outcome, bool StateIsFromThisRun, DateTimeOffset Finished)> RunSurveyCoreAsync(SurveyRunKind kind, DateTimeOffset started, CancellationToken cancellation)
    {
        if (!Settings.IsConfigured || Settings.IngestToken is not { } token)
        {
            return (Failure("Falta configurar la aplicación. Abrí la configuración e iniciá sesión."), false, DateTimeOffset.Now);
        }

        if (!AgentPackageInstaller.IsInstalled(AppPaths.AgentFolder))
        {
            return (Failure("El agente no está descargado. Abrí la configuración y tocá Actualizar agente."), false, DateTimeOffset.Now);
        }

        var dependencies = await EnsureDependenciesAsync(cancellation);
        if (!dependencies.Success) return (Failure(dependencies.Message), false, DateTimeOffset.Now);

        var node = NodeRuntime.Locate();
        if (!node.Found) return (Failure(node.Error), false, DateTimeOffset.Now);

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
            var cancelledAt = DateTimeOffset.Now;
            return (RunOutcome.From(kind, 1, new SurveyState { Status = SurveyStatus.Interrupted }, cancelledAt), false, cancelledAt);
        }
        catch (Exception exception)
        {
            _log.Write($"El relevamiento no pudo arrancar: {exception.Message}");
            return (Failure($"El relevamiento no pudo arrancar: {exception.Message}"), false, DateTimeOffset.Now);
        }

        var state = SurveyStateFile.Read(AppPaths.StateFile);

        // One instant for everything downstream of the script exiting (L-2): ResumesAt's preview below
        // and Remember's actual write must agree exactly, or a gap straddling a minute names the
        // balloon a minute earlier than the schedule, and a gap straddling midnight resets the streak
        // under the preview but not under the write. Returned as Finished so RunSurveyAsync can hand
        // this same value to Remember instead of reading the clock a second time.
        var now = DateTimeOffset.Now;

        // Moved ahead of building the outcome (it used to run after): a Blocked message has to name the
        // real resume time (TO-1), and that time comes from the same freshness check BlockBackoff.Next
        // already uses, so it has to exist before RunOutcome.From runs, not after. started is captured
        // before the process is even launched, so a state file this run actually wrote always carries a
        // startedAt at or after it; a file left over from an earlier run is always strictly before it.
        // null (no file, or one the script never touched this time) is not fresh either: there is
        // nothing to prove this run wrote anything.
        //
        // Known limitation, left alone: if the system clock steps backwards while this run is in
        // flight, a genuine wall this run just wrote can read as stale (stateStarted ends up before
        // started even though the file is this run's own). Bounded and self-correcting, the next tick
        // reads the clock the same way and moves on, and the pre-fix code had no equivalent protection
        // to lose, so not worth chasing here. The signal that would remove it: ProcessRunner already
        // knows the node process id it launched, and the script could write that same pid into the
        // state file, so freshness could be decided by "is this the pid we launched" instead of by
        // comparing clocks.
        var stateIsFromThisRun = state?.StartedAt is { } stateStarted && stateStarted >= started;

        // A state file this run did not write is not evidence of anything this run did (H1): reading it
        // as Blocked anyway used to leave a false "detenido, Mercado Libre pidió una verificación" in
        // Settings.LastRun (and, since a Blocked outcome always notifies, a false repeating balloon) on
        // every silent tick after a wall until the next run happened to touch the file again. Passing
        // null instead of the stale file is enough: RunOutcome.From/Resolve already treat a missing
        // state as "trust the exit code", exactly what a state this run never wrote deserves. This does
        // not touch BlockBackoff's own hold: Remember below still gates on the same stateIsFromThisRun,
        // so a stale read never re-escalates it either way.
        var effectiveState = stateIsFromThisRun ? state : null;
        var outcome = RunOutcome.From(kind, exitCode, effectiveState, now, ResumesAt(exitCode, effectiveState, stateIsFromThisRun, now));

        _log.Write($"Fin del relevamiento (código {exitCode}): {outcome.Message}");
        return (outcome, stateIsFromThisRun, now);
    }

    /// <summary>
    /// A preview of the hold Remember is about to persist a moment later, used only so a Blocked
    /// message can name the same resume time; Remember still owns the actual write, unchanged below.
    /// Null for every outcome but a fresh Blocked one: BlockBackoff.Next itself would leave a stale
    /// read's hold untouched (same stateIsFromThisRun gate as Remember's own call), so there is no new
    /// resume time to preview for it, and every non-Blocked outcome has no resume clause to fill in the
    /// first place.
    ///
    /// Previews the hold, then asks what the effective next automatic run would be with that hold in
    /// force (H-1): BB-4 makes the daily survey immune to a hold, so if the daily time falls inside the
    /// hold window, the real next automatic run is the sooner daily one, and the balloon has to say so,
    /// not the raw hold end. This is exactly NextRunAt's own question, asked with a hold that has not
    /// been persisted yet, which is why it goes through CurrentScheduleInputs' override instead.
    ///
    /// NextRunAt's own answer still needs two guards on top, both found in the second independent
    /// review's fix pass 2:
    /// - It reads Settings.LastDailyRun as it stands at preview time, still yesterday's date, because
    ///   Remember has not written today's run yet. For a daily run already overdue by the time the
    ///   script exits (a 06:45 daily time, script exits at 06:50) NextRunAt honestly reports 06:45, an
    ///   instant already in the past (item 1, blocking); only a candidate strictly after now is trusted,
    ///   anything else falls back to the raw previewed hold instead (see AResumesAtPreviewFallsBackTo
    ///   TheHeldHoldWhenTheDailyCandidateIsAlreadyPast and its non-regression sibling in
    ///   RunOutcomeTests).
    /// - It can answer null outright when nothing automatic is scheduled at all: paused, unlinked, or
    ///   both schedules switched off (item 3, low). Most reachable here via a manual "Relevar ahora" run
    ///   pressed while paused, which bypasses ScheduleDecider.Decide and can still hit a wall. The very
    ///   first version of this method ("?? previewedHold.Until") treated that null exactly like a stale
    ///   candidate and fell back to the raw previewed hold, promising a resume nothing automatic would
    ///   ever honour; null is the honest answer here too, same as RunOutcome.BlockedMessage already does
    ///   with a null resumesAt (AResumesAtOfNullLeavesTheResumeClauseOut, and see
    ///   AResumesAtPreviewNamesNoTimeAtAllWhenNothingAutomaticIsScheduled for this method's own case).
    /// </summary>
    private DateTimeOffset? ResumesAt(int exitCode, SurveyState? state, bool stateIsFromThisRun, DateTimeOffset now)
    {
        if (!stateIsFromThisRun || RunOutcome.Resolve(exitCode, state) != RunOutcomeKind.Blocked) return null;
        var current = new BlockBackoff.Hold(Settings.BlockedUntil, Settings.BlockedStreak, Settings.BlockedStreakDay);
        var previewedHold = BlockBackoff.Next(current, RunOutcomeKind.Blocked, stateIsFromThisRun: true, now, Settings.DailyTime);
        var next = ScheduleDecider.NextRunAt(CurrentScheduleInputs(now, previewedHold.Until));
        return next switch
        {
            null => null,
            { } at when at > now => at,
            _ => previewedHold.Until,
        };
    }

    private void Remember(SurveyRunKind kind, DateTimeOffset started, DateTimeOffset finished, RunOutcome outcome, bool stateIsFromThisRun)
    {
        // Settings.LastRun must reflect every run that actually produced evidence, not only the ones
        // worth a balloon (fix pass 2, item 2, medium): outcome.ShouldNotify alone used to gate this
        // (H-2, first fix pass), which happened to work only because every Pending run that served a
        // request also notifies, except one: web/scripts/meli-survey.mjs:1559 can serve a --pending
        // request, write the state file, and still exit 3 (every product already had today's snapshot),
        // which RunOutcome.From reads as RunOutcomeKind.Nothing, the same Kind as the true "nothing was
        // even pending" case (H-2) that never touches the state file. Gating on ShouldNotify alone
        // dropped that served run's own record too, so the settings window kept showing an older run
        // after a person pressed "Actualizar" on the web for a product already surveyed.
        // stateIsFromThisRun is exactly the signal that tells the two apart: true only when this run's
        // own script wrote the state file. The schedule still has to move on regardless of this gate, so
        // LastPendingRun/LastDailyRun and the backoff hold below are not gated by it, only what the
        // settings window shows is. Pinned at the RunOutcome level (this method has no test harness of
        // its own, see RunSurveyCoreAsync's own note) by RunOutcomeTests.
        // ARealRunsCountsSurviveAFollowingSilentPendingTick (the true nothing-due tick) and
        // AServedPendingRequestThatFoundNoNewListingsStillKeepsItsRecord (the served-but-empty run),
        // which mirror this exact gate line for line.
        if (outcome.Kind != RunOutcomeKind.Nothing || stateIsFromThisRun) Settings.LastRun = RunSummary.From(kind, started, finished, outcome);

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
        return RunOutcome.From(SurveyRunKind.Manual, 1, new SurveyState { Status = SurveyStatus.Error, Message = message }, DateTimeOffset.Now);
    }

    public void Dispose()
    {
        _running?.Cancel();
        _runGate.Dispose();
        _heartbeatGate.Dispose();
        _http.Dispose();
    }
}
