using System.Diagnostics;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;
using RudolphTech.Core.Web;
using RudolphTech.Services;

namespace RudolphTech;

/// <summary>
/// The one window of the app. Opened from the tray, always after the web password was accepted (or
/// straight away the very first time, when there is nothing configured yet and the password is
/// typed right here next to the address).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AgentService _agent;
    private readonly AppSettings _settings;

    private readonly TextBox _url = new() { Width = 330 };
    private readonly TextBox _password = new() { Width = 200, UseSystemPasswordChar = true };
    private readonly Button _login = new() { Text = "Iniciar sesión", Width = 120 };
    private readonly Label _session = new() { AutoSize = false, Width = 460, Height = 20, ForeColor = SystemColors.GrayText };
    private readonly Label _package = new() { AutoSize = false, Width = 460, Height = 20, ForeColor = SystemColors.GrayText };
    private readonly NumericUpDown _interval = new()
    {
        Minimum = AppSettings.MinimumPendingIntervalMinutes,
        Maximum = AppSettings.MaximumPendingIntervalMinutes,
        Width = 70,
    };
    private readonly DateTimePicker _daily = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 70,
    };
    private readonly CheckBox _chrome = new() { Text = "Chrome fuera de la pantalla", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Iniciar con Windows", AutoSize = true };
    private readonly Button _runNow = new() { Text = "Relevar ahora", Width = 140 };
    private readonly Button _update = new() { Text = "Actualizar agente", Width = 140 };
    private readonly Label _lastRun = new() { AutoSize = false, Width = 460, Height = 36 };
    private readonly LinkLabel _logs = new() { Text = "Abrir la carpeta del registro", AutoSize = true };
    private readonly Label _status = new() { AutoSize = false, Width = 460, Height = 20, ForeColor = SystemColors.GrayText };
    private readonly Button _close = new() { Text = "Cerrar", Width = 100 };

    public SettingsForm(AgentService agent)
    {
        _agent = agent;
        _settings = agent.Settings;

        Text = "Rudolph Tech";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(500, 470);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        BuildLayout();
        LoadFromSettings();
        RefreshStatus();

        _login.Click += OnLogin;
        _update.Click += OnUpdateAgent;
        _runNow.Click += OnRunNow;
        _logs.LinkClicked += (_, _) => OpenLogFolder();
        _close.Click += (_, _) => Close();
        _agent.RunningChanged += OnRunningChanged;
        _agent.RunFinished += OnRunFinished;
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var top = 16;
        Label Section(string text, int extraTop = 0)
        {
            top += extraTop;
            var label = new Label { Text = text, AutoSize = true, Location = new Point(16, top), Font = new Font(Font, FontStyle.Bold) };
            top += 24;
            return label;
        }

        var addressTitle = Section("Dirección de la aplicación");
        _url.Location = new Point(16, top);
        top += 30;
        _password.Location = new Point(16, top);
        _login.Location = new Point(226, top - 1);
        top += 30;
        _session.Location = new Point(16, top);
        top += 20;
        _package.Location = new Point(16, top);
        top += 8;

        var scheduleTitle = Section("Cuándo releva", 16);
        var intervalLabel = new Label { Text = "Atender pedidos cada", AutoSize = true, Location = new Point(16, top + 3) };
        _interval.Location = new Point(150, top);
        var minutesLabel = new Label { Text = "minutos", AutoSize = true, Location = new Point(228, top + 3) };
        top += 30;
        var dailyLabel = new Label { Text = "Relevamiento diario a las", AutoSize = true, Location = new Point(16, top + 3) };
        _daily.Location = new Point(180, top);
        top += 34;
        _chrome.Location = new Point(16, top);
        top += 26;
        _autostart.Location = new Point(16, top);
        top += 8;

        var actionsTitle = Section("Acciones", 16);
        _runNow.Location = new Point(16, top);
        _update.Location = new Point(166, top);
        top += 40;

        var lastRunTitle = Section("Último relevamiento");
        _lastRun.Location = new Point(16, top);
        top += 40;
        _logs.Location = new Point(16, top);
        top += 26;
        _status.Location = new Point(16, top);

        _close.Location = new Point(384, 424);

        Controls.AddRange(
        [
            addressTitle, _url, _password, _login, _session, _package,
            scheduleTitle, intervalLabel, _interval, minutesLabel, dailyLabel, _daily, _chrome, _autostart,
            actionsTitle, _runNow, _update,
            lastRunTitle, _lastRun, _logs, _status, _close,
        ]);
    }

    private void LoadFromSettings()
    {
        _url.Text = _settings.AppUrl;
        _interval.Value = Math.Clamp(_settings.PendingIntervalMinutes, _interval.Minimum, _interval.Maximum);
        _daily.Value = DateTime.Today.Add(_settings.DailyTime.ToTimeSpan());
        _chrome.Checked = _settings.ChromeOffScreen;
        _autostart.Checked = AutostartRegistry.IsEnabled();
    }

    private void ApplyToSettings()
    {
        if (AppUrl.TryNormalize(_url.Text, out var url)) _settings.AppUrl = url;
        _settings.PendingIntervalMinutes = (int)_interval.Value;
        _settings.DailyTime = TimeOnly.FromDateTime(_daily.Value);
        _settings.ChromeOffScreen = _chrome.Checked;
        _settings.StartWithWindows = _autostart.Checked;
        _agent.SaveSettings();
    }

    private void RefreshStatus()
    {
        _session.Text = StatusText.Session(_agent.HasSession, _settings.AppUrl);
        _package.Text = StatusText.PackageStatus(
            _settings.LastPackageDownload,
            Core.Agent.AgentPackageInstaller.IsInstalled(AppPaths.AgentFolder));
        _lastRun.Text = StatusText.LastRun(_settings.LastRun);
        _runNow.Enabled = !_agent.IsRunning && _settings.IsConfigured;
        _update.Enabled = !_agent.IsRunning;
        _runNow.Text = _agent.IsRunning ? "Relevando" : "Relevar ahora";
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        if (!AppUrl.TryNormalize(_url.Text, out var url))
        {
            Say("La dirección no es válida.");
            return;
        }

        if (_password.Text.Length == 0)
        {
            Say("Escribí la contraseña de la aplicación.");
            return;
        }

        _settings.AppUrl = url;
        _url.Text = url;
        Busy(true, "Iniciando sesión.");
        try
        {
            var result = await _agent.LoginAsync(url, _password.Text, CancellationToken.None);
            _password.Clear();
            if (!result.Success)
            {
                Say(result.Error);
                return;
            }

            Say("Sesión iniciada. Descargando el agente.");
            var (ok, message) = await _agent.RefreshPackageAsync(CancellationToken.None);
            Say(ok ? "Listo. El agente quedó al día." : message);
        }
        catch (Exception exception)
        {
            Say($"No se pudo iniciar sesión: {exception.Message}");
        }
        finally
        {
            Busy(false);
            RefreshStatus();
        }
    }

    private async void OnUpdateAgent(object? sender, EventArgs e)
    {
        if (!_agent.HasSession)
        {
            Say("Primero iniciá sesión con la contraseña de la aplicación.");
            return;
        }

        Busy(true, "Actualizando el agente.");
        try
        {
            var (ok, message) = await _agent.RefreshPackageAsync(CancellationToken.None);
            Say(ok ? "El agente quedó actualizado." : message);
        }
        catch (Exception exception)
        {
            Say($"No se pudo actualizar: {exception.Message}");
        }
        finally
        {
            Busy(false);
            RefreshStatus();
        }
    }

    private async void OnRunNow(object? sender, EventArgs e)
    {
        ApplyToSettings();
        Say("Arrancando el relevamiento. Se va a abrir una ventana de Chrome.");
        try
        {
            await _agent.RunSurveyAsync(SurveyRunKind.Manual, CancellationToken.None);
        }
        catch (Exception exception)
        {
            Say($"El relevamiento falló: {exception.Message}");
        }
    }

    private void OnRunningChanged(bool running) => RunOnUiThread(RefreshStatus);

    private void OnRunFinished(RunOutcome outcome) => RunOnUiThread(() =>
    {
        Say(outcome.Message);
        RefreshStatus();
    });

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        ApplyToSettings();
        if (_autostart.Checked != AutostartRegistry.IsEnabled())
        {
            AutostartRegistry.Set(_autostart.Checked, Environment.ProcessPath ?? Application.ExecutablePath);
        }
        _agent.RunningChanged -= OnRunningChanged;
        _agent.RunFinished -= OnRunFinished;
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
            Say($"No se pudo abrir la carpeta: {exception.Message}");
        }
    }

    private void Busy(bool busy, string? message = null)
    {
        _login.Enabled = !busy;
        _update.Enabled = !busy;
        _runNow.Enabled = !busy && !_agent.IsRunning;
        if (message is not null) Say(message);
    }

    private void Say(string message) => _status.Text = message;
}
