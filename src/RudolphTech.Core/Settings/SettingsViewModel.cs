using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Settings;

/// <summary>
/// Everything the settings window's Estado group and group enablement need, decided without
/// touching the window, a clock, or the disk. The window only wires labels and Enabled flags to
/// the values this returns for the current state.
/// </summary>
public sealed class SettingsViewModelInputs
{
    /// <summary> True once the app has a url and a stored ingest token (<see cref="AppSettings.IsConfigured"/>). </summary>
    public required bool Linked { get; init; }

    public required bool Running { get; init; }
    public RunSummary? LastRun { get; init; }
    public required bool DailyEnabled { get; init; }
    public required bool PendingEnabled { get; init; }

    /// <summary> False when the Node bundled with the app could not be found (see NodeLocator). </summary>
    public required bool NodeAvailable { get; init; }
}

public static class SettingsViewModel
{
    /// <summary> Shown instead of Reinstalá Rudolph Tech everywhere the bundled Node is missing. </summary>
    public const string NodeMissingMessage = "Falta el Node incluido. Reinstalá Rudolph Tech.";
    /// <summary> The one strong line at the top of the Estado group. </summary>
    public static string Headline(SettingsViewModelInputs inputs) => inputs.Linked
        ? "Todo listo. Rudolph Tech releva desde esta PC."
        : "Falta vincular esta PC con la aplicación.";

    /// <summary> The muted line under the headline, describing the last survey. </summary>
    public static string LastRunLine(SettingsViewModelInputs inputs)
    {
        if (!inputs.Linked) return NotLinkedExplanation;
        if (inputs.LastRun is not { } summary) return "Todavía no relevó nada.";

        var when = summary.FinishedAt ?? summary.StartedAt;
        var outcome = summary.Outcome switch
        {
            RunOutcomeKind.Finished => "sin errores",
            RunOutcomeKind.Nothing => "sin novedades",
            RunOutcomeKind.Blocked => "necesitó una verificación",
            RunOutcomeKind.Interrupted => "interrumpido",
            _ => "con errores",
        };

        return $"Último relevamiento: {when:dd/MM} a las {when:HH:mm}, {summary.Products} producto(s), {outcome}.";
    }

    /// <summary>
    /// "Relevar ahora" works only once the app is linked, never while one is already running, and
    /// never without the bundled Node: starting a run without it would only fail cryptically deep
    /// inside the process launch instead of here, where the person can actually see why.
    /// </summary>
    public static bool RunNowEnabled(SettingsViewModelInputs inputs) => inputs.Linked && !inputs.Running && inputs.NodeAvailable;

    public static string RunNowText(SettingsViewModelInputs inputs) => inputs.Running ? "Relevando..." : "Relevar ahora";

    /// <summary> The tooltip on "Relevar ahora" while it is disabled because Node is missing; empty otherwise. </summary>
    public static string RunNowTooltip(SettingsViewModelInputs inputs) => inputs.NodeAvailable ? "" : NodeMissingMessage;

    /// <summary>
    /// The Estado card's one primary button. Not linked, a disabled "Relevar ahora" left the person
    /// with no way in except a button buried in the Vinculación card below; "Vincular esta PC" here
    /// is that way in, front and center. Linked, this is exactly the "Relevar ahora" of before.
    /// </summary>
    public static string PrimaryActionText(SettingsViewModelInputs inputs) =>
        inputs.Linked ? RunNowText(inputs) : "Vincular esta PC";

    /// <summary>
    /// Linking talks to the web, not to the bundled Node: it never launches Chrome, so unlike
    /// "Relevar ahora" it does not need <see cref="SettingsViewModelInputs.NodeAvailable"/>, only
    /// that a survey is not already running.
    /// </summary>
    public static bool PrimaryActionEnabled(SettingsViewModelInputs inputs) =>
        inputs.Linked ? RunNowEnabled(inputs) : !inputs.Running;

    /// <summary> The missing-Node tooltip only ever applies to "Relevar ahora"; linking needs no Node. </summary>
    public static string PrimaryActionTooltip(SettingsViewModelInputs inputs) =>
        inputs.Linked ? RunNowTooltip(inputs) : "";

    /// <summary>
    /// The status line override for a missing Node. Only shown once linked: while not linked yet the
    /// window is already explaining that instead, and Node not being found is beside the point.
    /// </summary>
    public static string? NodeMissingStatus(SettingsViewModelInputs inputs) =>
        inputs.Linked && !inputs.NodeAvailable ? NodeMissingMessage : null;

    /// <summary> "Cuándo releva" only makes sense once the app knows where to report to. </summary>
    public static bool ScheduleGroupEnabled(SettingsViewModelInputs inputs) => inputs.Linked;

    /// <summary> "Opciones" (Chrome, autostart) only makes sense once the app is linked. </summary>
    public static bool OptionsGroupEnabled(SettingsViewModelInputs inputs) => inputs.Linked;

    /// <summary> The daily time picker only matters once the app is linked and the daily survey is on. </summary>
    public static bool DailyTimeEnabled(SettingsViewModelInputs inputs) => inputs.Linked && inputs.DailyEnabled;

    /// <summary> The minutes control only matters once the app is linked and the pending check is on. </summary>
    public static bool PendingControlsEnabled(SettingsViewModelInputs inputs) => inputs.Linked && inputs.PendingEnabled;

    /// <summary> The quiet explanation shown instead of the last run while not linked. </summary>
    public const string NotLinkedExplanation = "Vinculá esta PC para que pueda relevar.";

    /// <summary> Shown instead of a configured address when none was ever set. </summary>
    public const string NoAddressConfigured = "Todavía no está vinculada con ninguna aplicación.";

    /// <summary>
    /// The quiet line under "Vinculación con la aplicación". Linked, this is the existing "Vinculada
    /// con ... agente actualizado el ..." line. Not linked, it must never say "Vinculada": that word
    /// would contradict the headline and the pill right above it. It says instead what will happen
    /// (or that there is no address at all yet).
    /// </summary>
    public static string LinkSummary(SettingsViewModelInputs inputs, string? appUrl, DateTimeOffset? lastPackageDownload)
    {
        if (!inputs.Linked)
        {
            return string.IsNullOrWhiteSpace(appUrl) ? NoAddressConfigured : $"Se vinculará con {appUrl}.";
        }

        return StatusText.LinkSummary(appUrl ?? "", lastPackageDownload);
    }
}
