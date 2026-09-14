using System.Diagnostics;
using System.Windows;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;
using RudolphTech.Core.Web;
using RudolphTech.Services;

namespace RudolphTech.Settings;

/// <summary>
/// The one window of the app. Opened from the tray, always after the same password prompt (see
/// PasswordWindow and TrayApplicationContext): the prompt is what links this PC the first time, or
/// simply confirms who is looking otherwise. That one password is kept here in memory for the
/// lifetime of this window (never persisted) and reused for Actualizar agente and Volver a
/// vincular, so it is never asked twice.
///
/// Every "what should this say" or "should this be enabled" decision comes from
/// RudolphTech.Core.Settings.SettingsViewModel and LinkFlow, republished by
/// <see cref="SettingsWindowViewModel"/>; this code behind only moves data in and out and calls
/// AgentService.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AgentService _agent;
    private readonly AppSettings _settings;

    /// <summary> The password typed in the prompt that opened this window. Never written to disk. </summary>
    private readonly string? _password;

    private readonly SettingsWindowViewModel _vm = new();

    private bool _initialAutostart;

    public SettingsWindow(AgentService agent, string? password)
    {
        _agent = agent;
        _settings = agent.Settings;
        _password = password;

        InitializeComponent();
        DataContext = _vm;

        _vm.HasPasswordInMemory = !string.IsNullOrEmpty(_password);

        LoadFromSettings();
        RefreshStatus();

        _agent.RunningChanged += OnRunningChanged;
        _agent.RunFinished += OnRunFinished;
        Closing += OnClosing;
        Loaded += async (_, _) => await LinkOnOpenIfNeededAsync();
    }

    private void LoadFromSettings()
    {
        _vm.Linked = _settings.IsConfigured;
        _vm.DailyEnabled = _settings.DailyEnabled;
        _vm.DailyTimeText = _settings.DailyTime.ToString("HH:mm");
        _vm.PendingEnabled = _settings.PendingEnabled;
        _vm.PendingIntervalText = _settings.PendingIntervalMinutes.ToString();
        _vm.ChromeOffScreen = _settings.ChromeOffScreen;
        _initialAutostart = AutostartRegistry.IsEnabled();
        _vm.StartWithWindows = _initialAutostart;
        _vm.AppUrl = _settings.AppUrl;
        _vm.LastPackageDownload = _settings.LastPackageDownload;
        _vm.LastRun = _settings.LastRun;
        _vm.NodeAvailable = NodeRuntime.Locate().Found;
    }

    private void ApplyToSettings()
    {
        _settings.DailyEnabled = _vm.DailyEnabled;
        _settings.DailyTime = _vm.DailyTime;
        _settings.PendingEnabled = _vm.PendingEnabled;
        _settings.PendingIntervalMinutes = _vm.PendingIntervalMinutes;
        _settings.ChromeOffScreen = _vm.ChromeOffScreen;
        _settings.StartWithWindows = _vm.StartWithWindows;
        _agent.SaveSettings();
    }

    private LinkFlowInputs LinkInputs() => new()
    {
        Linked = _settings.IsConfigured,
        HasPasswordInMemory = !string.IsNullOrEmpty(_password),
    };

    private void RefreshStatus()
    {
        _vm.Linked = _settings.IsConfigured;
        _vm.Running = _agent.IsRunning;
        _vm.LastRun = _settings.LastRun;
        _vm.AppUrl = _settings.AppUrl;
        _vm.LastPackageDownload = _settings.LastPackageDownload;

        var missingNode = _vm.NodeMissingStatus;
        if (missingNode is not null) _vm.SetStatus(missingNode, isError: true);
    }

    /// <summary> The very first thing the window does once shown, if it opened not yet linked. </summary>
    private async Task LinkOnOpenIfNeededAsync()
    {
        if (!LinkFlow.ShouldLinkOnOpen(LinkInputs())) return;

        var (_, message) = await LinkAsync(_settings.AppUrl, "Vinculando esta PC...", "Listo. Esta PC quedó vinculada.");
        _vm.SetStatus(message);
    }

    /// <summary>
    /// Logs in again with the password kept from the opening prompt (the session may have gone stale
    /// while the window sat open) and then downloads the package. The one place that talks to the
    /// web on behalf of Actualizar agente and Volver a vincular.
    /// </summary>
    private async Task<(bool Ok, string Message)> LinkAsync(string url, string busyMessage, string successMessage)
    {
        if (_password is not { Length: > 0 } password)
        {
            return (false, "Hace falta la contraseña. Cerrá y volvé a abrir la configuración.");
        }

        Busy(true, busyMessage);
        try
        {
            var login = await _agent.LoginAsync(url, password, CancellationToken.None);
            if (!login.Success)
            {
                return (false, login.Offline ? "Sin conexión con la aplicación. Probá de nuevo cuando vuelva internet." : login.Error);
            }

            var (ok, message) = await _agent.RefreshPackageAsync(CancellationToken.None);
            return ok ? (true, successMessage) : (false, message);
        }
        catch (Exception exception)
        {
            return (false, $"No se pudo vincular: {exception.Message}");
        }
        finally
        {
            Busy(false);
            RefreshStatus();
        }
    }

    private void OnOpenBrowser(object sender, RoutedEventArgs e)
    {
        var (url, error) = BrowserLink.Resolve(_settings.AppUrl);
        if (error is not null)
        {
            _vm.SetStatus(error, isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _vm.SetStatus($"No se pudo abrir el navegador: {exception.Message}", isError: true);
        }
    }

    private async void OnUpdateAgent(object sender, RoutedEventArgs e)
    {
        var (ok, message) = await LinkAsync(_settings.AppUrl, "Actualizando el agente.", "El agente quedó actualizado.");
        _vm.SetStatus(message, isError: !ok);
    }

    private async void OnRelink(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            this,
            $"¿Volver a vincular esta PC con la aplicación {_settings.AppUrl}?",
            "Rudolph Tech",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var (ok, message) = await LinkAsync(_settings.AppUrl, "Vinculando esta PC de nuevo...", "Esta PC quedó vinculada de nuevo.");
        _vm.SetStatus(message, isError: !ok);
    }

    /// <summary>
    /// The Estado card's one primary button. Not linked, it links this PC (the same flow "Volver a
    /// vincular" used to run); linked, it is exactly the old "Relevar ahora". Which one it means
    /// right now comes straight off the view model, the same source its own label and enabled state
    /// already read from (RudolphTech.Core.Settings.SettingsViewModel.PrimaryActionText/Enabled).
    /// </summary>
    private async void OnPrimaryAction(object sender, RoutedEventArgs e)
    {
        if (!_vm.Linked)
        {
            var (ok, message) = await LinkAsync(_settings.AppUrl, "Vinculando esta PC...", "Listo. Esta PC quedó vinculada.");
            _vm.SetStatus(message, isError: !ok);
            return;
        }

        ApplyToSettings();

        _vm.NodeAvailable = NodeRuntime.Locate().Found;
        if (!_vm.NodeAvailable)
        {
            _vm.SetStatus(Core.Settings.SettingsViewModel.NodeMissingMessage, isError: true);
            return;
        }

        _vm.SetStatus("Arrancando el relevamiento. Se va a abrir una ventana de Chrome.");
        try
        {
            await _agent.RunSurveyAsync(SurveyRunKind.Manual, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _vm.SetStatus($"El relevamiento falló: {exception.Message}", isError: true);
        }
    }

    /// <summary>
    /// The quiet link in Vinculación, shown only before this PC is linked (see
    /// RudolphTech.Core.Settings.LinkFlow.ChangeAddressVisible). A local, offline edit: unlike
    /// linking itself, changing the configured address needs no password and talks to no server,
    /// only RudolphTech.Core.Web.AppUrl.TryNormalize to reject something unusable.
    /// </summary>
    private void OnChangeAddress(object sender, RoutedEventArgs e)
    {
        var dialog = new TextPromptWindow(
            "Dirección de la aplicación",
            "Escribí la dirección de Rudolph con la que esta PC va a vincularse.",
            _settings.AppUrl,
            value => Core.Web.AppUrl.TryNormalize(value, out var normalized)
                ? (true, normalized)
                : (false, "Escribí una dirección válida, por ejemplo https://rudolph-mvp.vercel.app."))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true || dialog.AcceptedValue is not { } normalizedUrl) return;

        _settings.AppUrl = normalizedUrl;
        _agent.SaveSettings();
        _vm.AppUrl = normalizedUrl;
        _vm.SetStatus($"Se va a vincular con {normalizedUrl}.");
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            Process.Start(new ProcessStartInfo(AppPaths.LogFolder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _vm.SetStatus($"No se pudo abrir la carpeta del registro: {exception.Message}", isError: true);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnRunningChanged(bool running) => RunOnUiThread(RefreshStatus);

    private void OnRunFinished(RunOutcome outcome) => RunOnUiThread(() =>
    {
        _vm.SetStatus(outcome.Message);
        RefreshStatus();
    });

    private void RunOnUiThread(Action action)
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(action);
        else action();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ApplyToSettings();
        if (_vm.StartWithWindows != _initialAutostart)
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            AutostartRegistry.Set(_vm.StartWithWindows, executablePath);
        }
        _agent.RunningChanged -= OnRunningChanged;
        _agent.RunFinished -= OnRunFinished;
    }

    private void Busy(bool busy, string? message = null)
    {
        _vm.Busy = busy;
        if (message is not null) _vm.SetStatus(message);
    }
}
