using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using RudolphTech.Core.Settings;
using RudolphTech.Core.Survey;

namespace RudolphTech.Settings;

/// <summary>
/// The WPF-bindable shell around the pure decisions in RudolphTech.Core.Settings.SettingsViewModel
/// and LinkFlow. Every "what should this say" or "should this be enabled" answer still comes from
/// Core; this class only holds the current state as plain fields the window sets from AppSettings
/// and AgentService, and republishes the Core answers as INotifyPropertyChanged properties the
/// window's XAML binds to. No decision is made here that Core does not already own.
/// </summary>
public sealed class SettingsWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ----- Raw state, set by the window -----
    private bool _linked;
    private bool _running;
    private bool _busy;
    private RunSummary? _lastRun;
    private bool _dailyEnabled;
    private bool _pendingEnabled;
    private bool _nodeAvailable = true;
    private bool _hasPasswordInMemory;
    private string _appUrl = AppSettings.DefaultAppUrl;
    private DateTimeOffset? _lastPackageDownload;
    private TimeOnly _dailyTime = AppSettings.DefaultDailyTime;
    private int _pendingIntervalMinutes = AppSettings.DefaultPendingIntervalMinutes;
    private bool _chromeOffScreen;
    private bool _startWithWindows;
    private string _statusMessage = "";
    private bool _statusIsError;

    private static readonly System.Windows.Media.Brush OkBackground = Frozen(0xE6, 0xF4, 0xEA);
    private static readonly System.Windows.Media.Brush OkForeground = Frozen(0x1E, 0x7B, 0x34);
    private static readonly System.Windows.Media.Brush WarnBackground = Frozen(0xFF, 0xF4, 0xE0);
    private static readonly System.Windows.Media.Brush WarnForeground = Frozen(0x8A, 0x5A, 0x00);
    private static readonly System.Windows.Media.Brush ErrorBackground = Frozen(0xFD, 0xEA, 0xEA);
    private static readonly System.Windows.Media.Brush ErrorForeground = Frozen(0xC8, 0x10, 0x2E);

    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private SettingsViewModelInputs ViewModelInputs() => new()
    {
        Linked = _linked,
        Running = _running,
        LastRun = _lastRun,
        DailyEnabled = _dailyEnabled,
        PendingEnabled = _pendingEnabled,
        NodeAvailable = _nodeAvailable,
    };

    private LinkFlowInputs LinkInputs() => new() { Linked = _linked, HasPasswordInMemory = _hasPasswordInMemory };

    /// <summary> Every bound property this class computes changes together whenever the raw state does. </summary>
    private void Recompute()
    {
        Raise(nameof(Headline));
        Raise(nameof(LastRunLine));
        Raise(nameof(PrimaryActionEnabled));
        Raise(nameof(PrimaryActionText));
        Raise(nameof(PrimaryActionTooltip));
        Raise(nameof(ScheduleGroupEnabled));
        Raise(nameof(OptionsGroupEnabled));
        Raise(nameof(DailyTimeEnabled));
        Raise(nameof(PendingControlsEnabled));
        Raise(nameof(UpdateAgentEnabled));
        Raise(nameof(RelinkEnabled));
        Raise(nameof(WebActionsVisible));
        Raise(nameof(ChangeAddressVisible));
        Raise(nameof(ChangeAddressEnabled));
        Raise(nameof(NodeMissingStatus));
        Raise(nameof(PillText));
        Raise(nameof(PillBackground));
        Raise(nameof(PillForeground));
        Raise(nameof(LinkSummary));
    }

    public bool Linked { get => _linked; set { if (_linked == value) return; _linked = value; Recompute(); } }
    public bool Running { get => _running; set { if (_running == value) return; _running = value; Recompute(); } }
    public bool Busy { get => _busy; set { if (_busy == value) return; _busy = value; Recompute(); } }
    public RunSummary? LastRun { get => _lastRun; set { _lastRun = value; Recompute(); } }
    public bool NodeAvailable { get => _nodeAvailable; set { if (_nodeAvailable == value) return; _nodeAvailable = value; Recompute(); } }
    public bool HasPasswordInMemory { get => _hasPasswordInMemory; set { if (_hasPasswordInMemory == value) return; _hasPasswordInMemory = value; Recompute(); } }

    public bool DailyEnabled { get => _dailyEnabled; set { if (_dailyEnabled == value) return; _dailyEnabled = value; Recompute(); } }
    public bool PendingEnabled { get => _pendingEnabled; set { if (_pendingEnabled == value) return; _pendingEnabled = value; Recompute(); } }
    public bool ChromeOffScreen { get => _chromeOffScreen; set { if (_chromeOffScreen == value) return; _chromeOffScreen = value; Raise(); } }
    public bool StartWithWindows { get => _startWithWindows; set { if (_startWithWindows == value) return; _startWithWindows = value; Raise(); } }

    public string AppUrl { get => _appUrl; set { if (_appUrl == value) return; _appUrl = value; Raise(nameof(LinkSummary)); } }
    public DateTimeOffset? LastPackageDownload { get => _lastPackageDownload; set { _lastPackageDownload = value; Raise(nameof(LinkSummary)); } }

    /// <summary> The daily time as HH:mm text, editable in place; an invalid entry is simply not applied. </summary>
    public string DailyTimeText
    {
        get => _dailyTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        set
        {
            if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                _dailyTime = parsed;
            }
            Raise();
        }
    }

    public TimeOnly DailyTime => _dailyTime;

    /// <summary> The pending check interval as plain digits, clamped into range on every valid entry. </summary>
    public string PendingIntervalText
    {
        get => _pendingIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _pendingIntervalMinutes = Math.Clamp(parsed, AppSettings.MinimumPendingIntervalMinutes, AppSettings.MaximumPendingIntervalMinutes);
            }
            Raise();
        }
    }

    public int PendingIntervalMinutes => _pendingIntervalMinutes;

    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; Raise(); } }
    public bool StatusIsError { get => _statusIsError; private set { _statusIsError = value; Raise(); } }

    public void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    // ----- Computed, straight from Core -----
    public string Headline => Core.Settings.SettingsViewModel.Headline(ViewModelInputs());
    public string LastRunLine => Core.Settings.SettingsViewModel.LastRunLine(ViewModelInputs());
    /// <summary> The Estado card's one primary button: "Vincular esta PC" not linked, "Relevar ahora" linked. </summary>
    public bool PrimaryActionEnabled => Core.Settings.SettingsViewModel.PrimaryActionEnabled(ViewModelInputs()) && !_busy;
    public string PrimaryActionText => Core.Settings.SettingsViewModel.PrimaryActionText(ViewModelInputs());
    public string? PrimaryActionTooltip
    {
        get
        {
            var tooltip = Core.Settings.SettingsViewModel.PrimaryActionTooltip(ViewModelInputs());
            return string.IsNullOrEmpty(tooltip) ? null : tooltip;
        }
    }

    public bool ScheduleGroupEnabled => Core.Settings.SettingsViewModel.ScheduleGroupEnabled(ViewModelInputs());
    public bool OptionsGroupEnabled => Core.Settings.SettingsViewModel.OptionsGroupEnabled(ViewModelInputs());
    public bool DailyTimeEnabled => Core.Settings.SettingsViewModel.DailyTimeEnabled(ViewModelInputs());
    public bool PendingControlsEnabled => Core.Settings.SettingsViewModel.PendingControlsEnabled(ViewModelInputs());

    /// <summary> Node missing is surfaced through the status line, not just the disabled button. </summary>
    public string? NodeMissingStatus => Core.Settings.SettingsViewModel.NodeMissingStatus(ViewModelInputs());

    public bool UpdateAgentEnabled => Core.Settings.LinkFlow.WebActionsEnabled(LinkInputs()) && !_running && !_busy;
    public bool RelinkEnabled => Core.Settings.LinkFlow.WebActionsEnabled(LinkInputs()) && !_running && !_busy;

    /// <summary> Whether Actualizar agente / Volver a vincular show at all: only once already linked. </summary>
    public bool WebActionsVisible => Core.Settings.LinkFlow.WebActionsVisible(LinkInputs());

    /// <summary> The quiet "Cambiar la dirección de la aplicación" link: only before there is a link yet. </summary>
    public bool ChangeAddressVisible => Core.Settings.LinkFlow.ChangeAddressVisible(LinkInputs());

    public bool ChangeAddressEnabled => ChangeAddressVisible && !_busy;

    public string LinkSummary => Core.Settings.SettingsViewModel.LinkSummary(ViewModelInputs(), _appUrl, _lastPackageDownload);

    /// <summary> Todo listo (green), Falta vincular (amber), or an error (red, the bundled Node missing). </summary>
    public string PillText => !_linked ? "Falta vincular" : (_nodeAvailable ? "Todo listo" : "Error");

    public System.Windows.Media.Brush PillBackground => !_linked ? WarnBackground : (_nodeAvailable ? OkBackground : ErrorBackground);
    public System.Windows.Media.Brush PillForeground => !_linked ? WarnForeground : (_nodeAvailable ? OkForeground : ErrorForeground);
}
