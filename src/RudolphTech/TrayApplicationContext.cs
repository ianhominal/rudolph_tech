using System.Diagnostics;
using RudolphTech.Core.Logging;
using RudolphTech.Core.Scheduling;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;
using RudolphTech.Core.Web;
using RudolphTech.Services;
using RudolphTech.Settings;

namespace RudolphTech;

/// <summary>
/// The app itself: an icon next to the clock, a menu, and a timer that decides when to run the
/// survey. It replaces the two Windows Scheduled Tasks the old zip installed, and it deliberately
/// runs inside the person's own session, because Mercado Libre only answers a real visible Chrome
/// window and a Windows service has no desktop to open one on.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary> How often the schedule is looked at. The decision itself lives in ScheduleDecider. </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the web app is told this program is still open. Everything that changes what it would
    /// say (starting up, a run beginning or ending, pausing) reports itself right away, so this is only
    /// the keepalive that keeps the web from calling the program closed. The web gives it six minutes
    /// of silence before it does (web/src/lib/agent-presence.ts), which is room for two lost ones.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(2);

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AgentService _agent;
    private readonly AppSettings _settings;
    private readonly LogWriter _log;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _runItem;

    private readonly EventWaitHandle _exitSignal;
    private readonly RegisteredWaitHandle _exitWait;

    private SettingsWindow? _window;
    private DateTimeOffset? _lastHeartbeat;
    private bool _heartbeatFailing;

    public TrayApplicationContext()
    {
        AppPaths.EnsureCreated();
        _log = new LogWriter(AppPaths.LogFolder);
        LogWriter.Prune(AppPaths.LogFolder, DateTimeOffset.Now);

        var store = new SettingsStore(AppPaths.SettingsFile, new DpapiSecretProtector());
        _settings = store.Load();
        _agent = new AgentService(store, _settings, _log);

        // A run killed halfway (a power cut, the person logging off) could have left the generated
        // .env behind with the ingest token in it.
        Core.Agent.AgentPackageInstaller.RemoveEnvFile(AppPaths.AgentFolder);

        _pauseItem = new ToolStripMenuItem(_settings.Paused ? "Reanudar" : "Pausar", null, (_, _) => TogglePause());
        _runItem = new ToolStripMenuItem("Relevar ahora", null, (_, _) => StartManualRun());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Abrir Rudolph en el navegador", null, (_, _) => OpenInBrowser()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir configuración", null, (_, _) => OpenSettings()));
        menu.Items.Add(_runItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Ver registro", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => ExitApplication()));

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = StatusText.TrayTooltip(_settings, _settings.Paused, running: false, DateTimeOffset.Now),
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _agent.RunningChanged += OnRunningChanged;
        _agent.RunFinished += OnRunFinished;

        _timer = new System.Windows.Forms.Timer { Interval = (int)TickInterval.TotalMilliseconds };
        _timer.Tick += OnTick;
        _timer.Start();

        // "RudolphTech.exe --salir" (what the uninstaller runs) sets this, so the app closes the
        // same way the Salir menu item does instead of being killed with its files half written.
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitSignal,
            (_, _) => RunOnUiThread(() => ExitApplication(confirm: false)),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: true);

        _log.Write("Rudolph Tech arrancó.");
        // Before anything else: the web app has no other way of finding out this PC came back.
        SendHeartbeat();
        if (!_settings.IsConfigured) BeginInvokeFirstRun();
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "rudolph.ico");
        try
        {
            if (File.Exists(path)) return new Icon(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            // Falls through to the stock icon below.
        }
        return SystemIcons.Application;
    }

    /// <summary>
    /// The very first time there is nothing configured, so the window opens by itself. It still asks
    /// for the password first, the same way opening it by hand does: that one password is what links
    /// the PC once the window is up.
    /// </summary>
    private void BeginInvokeFirstRun()
    {
        var starter = new System.Windows.Forms.Timer { Interval = 500 };
        starter.Tick += (_, _) =>
        {
            starter.Stop();
            starter.Dispose();
            OpenSettings();
        };
        starter.Start();
    }

    /// <summary>
    /// Reports to the web app, without ever getting in the way: it is started and forgotten, and a
    /// failure is only worth one line in the log the first time it happens. A survey running for
    /// twenty minutes must keep being reported, which is why this is not tied to the schedule.
    ///
    /// AgentService reads the state after taking its own gate, so calls that overlap (the tick's, and
    /// the one a run starting raises straight afterwards) still leave the newest state on the web.
    /// </summary>
    private async void SendHeartbeat()
    {
        _lastHeartbeat = DateTimeOffset.Now;
        try
        {
            var outcome = await _agent.SendHeartbeatAsync(CancellationToken.None);
            // Nothing to say before the PC is linked: there is no address and no token to say it with, and
            // "no se pudo avisarle a la web" as the first line of a fresh install points at the wrong thing.
            if (outcome == HeartbeatOutcome.NotLinked) return;

            var failing = outcome == HeartbeatOutcome.Failed;
            if (failing != _heartbeatFailing)
            {
                // Only the change is worth saying: this fires every couple of minutes all day.
                _heartbeatFailing = failing;
                _log.Write(failing
                    ? "No se pudo avisarle a la web que el programa está abierto. Se reintenta solo."
                    : "Se restableció el aviso a la web.");
            }
        }
        catch (Exception exception)
        {
            _log.Write($"El aviso a la web falló: {exception.Message}");
        }
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        if (_lastHeartbeat is not { } last || now - last >= HeartbeatInterval) SendHeartbeat();

        var action = ScheduleDecider.Decide(_agent.CurrentScheduleInputs(now));

        if (action == ScheduledAction.None) return;

        try
        {
            // Before the full daily survey, bring the scripts in step with the deployed web app.
            if (action == ScheduledAction.Daily) await _agent.RefreshPackageIfDueAsync(CancellationToken.None);

            await _agent.RunSurveyAsync(
                action == ScheduledAction.Daily ? SurveyRunKind.Daily : SurveyRunKind.Pending,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _log.Write($"El relevamiento programado falló: {exception.Message}");
        }
    }

    private void OnRunningChanged(bool running)
    {
        RunOnUiThread(() =>
        {
            _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, running, DateTimeOffset.Now);
            _runItem.Enabled = !running;
            _runItem.Text = running ? "Relevando" : "Relevar ahora";
            // A run starting or ending changes both halves of what the web shows, so it says so now
            // rather than up to two minutes later.
            SendHeartbeat();
        });
    }

    private void OnRunFinished(RunOutcome outcome)
    {
        RunOnUiThread(() =>
        {
            if (!outcome.ShouldNotify) return;
            var icon = outcome.Kind switch
            {
                RunOutcomeKind.Finished => ToolTipIcon.Info,
                RunOutcomeKind.Nothing => ToolTipIcon.Info,
                RunOutcomeKind.Error => ToolTipIcon.Error,
                _ => ToolTipIcon.Warning,
            };
            _tray.ShowBalloonTip(8000, outcome.Title, outcome.Message, icon);
        });
    }

    private void RunOnUiThread(Action action)
    {
        if (_tray.ContextMenuStrip is { } menu && menu.InvokeRequired) menu.BeginInvoke(action);
        else action();
    }

    /// <summary>
    /// Opening the settings always asks for the web password first, whether the PC is already linked
    /// (to see the configuration) or not (to link it). There is only one password: whatever is typed
    /// here is what the settings window reuses in memory for Actualizar agente and Volver a vincular,
    /// so it is never asked twice.
    /// </summary>
    private void OpenSettings()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var promptText = LinkFlow.PromptText(_settings.IsConfigured);
        var prompt = new PasswordWindow(_settings.AppUrl, promptText, VerifyPasswordAsync);

        if (prompt.ShowDialog() == true) ShowWindow(prompt.VerifiedPassword);
    }

    private async Task<(bool Ok, string Error)> VerifyPasswordAsync(string password, CancellationToken cancellation)
    {
        var result = await _agent.LoginAsync(_settings.AppUrl, password, cancellation);
        if (result.Success) return (true, "");
        return (false, result.Offline ? "Sin conexión con la aplicación. Probá de nuevo cuando vuelva internet." : result.Error);
    }

    private void ShowWindow(string? password)
    {
        _window = new SettingsWindow(_agent, password);
        _window.Closed += (_, _) =>
        {
            _window = null;
            _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, _agent.IsRunning, DateTimeOffset.Now);
            _pauseItem.Text = _settings.Paused ? "Reanudar" : "Pausar";
        };
        _window.Show();
        _window.Activate();
    }

    private async void StartManualRun()
    {
        if (!_settings.IsConfigured)
        {
            _tray.ShowBalloonTip(6000, "Falta configurar", "Abrí la configuración e iniciá sesión primero.", ToolTipIcon.Warning);
            return;
        }

        try
        {
            await _agent.RunSurveyAsync(SurveyRunKind.Manual, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _log.Write($"El relevamiento manual falló: {exception.Message}");
        }
    }

    private void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _agent.SaveSettings();
        _pauseItem.Text = _settings.Paused ? "Reanudar" : "Pausar";
        _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, _agent.IsRunning, DateTimeOffset.Now);
        SendHeartbeat();
        _log.Write(_settings.Paused ? "Relevamientos en pausa." : "Relevamientos reanudados.");
        _tray.ShowBalloonTip(
            4000,
            "Rudolph Tech",
            _settings.Paused ? "No va a arrancar ningún relevamiento automático." : StatusText.Schedule(_settings, paused: false, DateTimeOffset.Now),
            ToolTipIcon.Info);
    }

    private void OpenInBrowser()
    {
        var (url, error) = BrowserLink.Resolve(_settings.AppUrl);
        if (error is not null)
        {
            _tray.ShowBalloonTip(6000, "Rudolph Tech", error, ToolTipIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _log.Write($"No se pudo abrir el navegador: {exception.Message}");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            Process.Start(new ProcessStartInfo(AppPaths.LogFolder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _log.Write($"No se pudo abrir la carpeta del registro: {exception.Message}");
        }
    }

    private void ExitApplication(bool confirm = true)
    {
        if (confirm && _agent.IsRunning)
        {
            var answer = MessageBox.Show(
                "Hay un relevamiento en curso. Si salís ahora se corta y se cierra la ventana de Chrome. ¿Salir igual?",
                "Rudolph Tech",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        if (_agent.IsRunning) _agent.StopRunningSurvey();

        _log.Write("Rudolph Tech se cerró.");
        _timer.Stop();
        _tray.Visible = false;

        // Not ExitThread(): that ends a Windows Forms message loop, and there is none. What is running is
        // WPF's, started by Program.cs's app.Run(), and App.xaml declares ShutdownMode="OnExplicitShutdown"
        // so nothing but this call ends it. Getting this wrong is silent and nasty: "Salir" hid the tray
        // icon, stopped the timer, wrote "Rudolph Tech se cerró" in the log, and left the process alive and
        // invisible, holding the single instance mutex so the next start answered "ya está abierto".
        // Through the dispatcher because the exit signal (RudolphTech.exe --salir) arrives on a thread pool
        // thread, and Shutdown belongs to the thread that owns the loop.
        if (System.Windows.Application.Current is { } app) app.Dispatcher.Invoke(app.Shutdown);
        else ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exitWait.Unregister(_exitSignal);
            _exitSignal.Dispose();
            _timer.Dispose();
            _tray.Dispose();
            _agent.Dispose();
        }
        base.Dispose(disposing);
    }
}
