using System.Diagnostics;
using RudolphTech.Core.Logging;
using RudolphTech.Core.Scheduling;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;
using RudolphTech.Services;

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

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AgentService _agent;
    private readonly AppSettings _settings;
    private readonly LogWriter _log;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _runItem;

    private readonly EventWaitHandle _exitSignal;
    private readonly RegisteredWaitHandle _exitWait;

    private SettingsForm? _window;

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
            Text = StatusText.TrayTooltip(_settings, _settings.Paused, running: false),
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

    /// <summary> The very first time there is nothing configured, so the window opens by itself. </summary>
    private void BeginInvokeFirstRun()
    {
        var starter = new System.Windows.Forms.Timer { Interval = 500 };
        starter.Tick += (_, _) =>
        {
            starter.Stop();
            starter.Dispose();
            ShowWindow();
        };
        starter.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        var action = ScheduleDecider.Decide(new ScheduleInputs
        {
            Now = DateTimeOffset.Now,
            PendingIntervalMinutes = _settings.PendingIntervalMinutes,
            DailyTime = _settings.DailyTime,
            Paused = _settings.Paused,
            Configured = _settings.IsConfigured,
            IsRunning = _agent.IsRunning,
            LastPendingRun = _settings.LastPendingRun,
            LastDailyRun = _settings.LastDailyRun,
        });

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
            _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, running);
            _runItem.Enabled = !running;
            _runItem.Text = running ? "Relevando" : "Relevar ahora";
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

    /// <summary> Opening the settings asks for the web password every time, unless nothing is configured yet. </summary>
    private void OpenSettings()
    {
        if (_window is { IsDisposed: false })
        {
            _window.Activate();
            return;
        }

        if (!_settings.IsConfigured)
        {
            ShowWindow();
            return;
        }

        using var prompt = new PasswordPromptForm(_settings.AppUrl, VerifyPasswordAsync);
        if (prompt.ShowDialog() == DialogResult.OK) ShowWindow();
    }

    private async Task<(bool Ok, string Error)> VerifyPasswordAsync(string password, CancellationToken cancellation)
    {
        var result = await _agent.LoginAsync(_settings.AppUrl, password, cancellation);
        if (result.Success) return (true, "");
        return (false, result.Offline ? "Sin conexión con la aplicación. Probá de nuevo cuando vuelva internet." : result.Error);
    }

    private void ShowWindow()
    {
        _window = new SettingsForm(_agent);
        _window.FormClosed += (_, _) =>
        {
            _window = null;
            _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, _agent.IsRunning);
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
        _tray.Text = StatusText.TrayTooltip(_settings, _settings.Paused, _agent.IsRunning);
        _log.Write(_settings.Paused ? "Relevamientos en pausa." : "Relevamientos reanudados.");
        _tray.ShowBalloonTip(
            4000,
            "Rudolph Tech",
            _settings.Paused ? "No va a arrancar ningún relevamiento automático." : StatusText.Schedule(_settings, paused: false),
            ToolTipIcon.Info);
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
        ExitThread();
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
