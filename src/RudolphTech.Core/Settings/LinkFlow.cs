namespace RudolphTech.Core.Settings;

/// <summary>
/// There is only one password, the web one, and it is asked exactly once: in the prompt that opens
/// the settings window. Everything else that needs the web (linking this PC the first time,
/// Actualizar agente, Volver a vincular) reuses that same password from memory instead of asking
/// again. This decides the prompt's wording, whether opening the window should link right away, and
/// whether the web actions inside it can run, all as pure functions of the current state so nothing
/// here depends on a clock, the disk, or a real password.
/// </summary>
public sealed class LinkFlowInputs
{
    /// <summary> True once the app has a url and a stored ingest token (<see cref="AppSettings.IsConfigured"/>). </summary>
    public required bool Linked { get; init; }

    /// <summary> True once the password typed in the opening prompt is held in memory for this window. </summary>
    public required bool HasPasswordInMemory { get; init; }
}

public static class LinkFlow
{
    /// <summary> What the password prompt says before the settings window opens. </summary>
    public static string PromptText(bool linked) => linked
        ? "Ingresá la contraseña de la aplicación para ver la configuración."
        : "Ingresá la contraseña de la aplicación para vincular esta PC.";

    /// <summary>
    /// Whether the settings window should start linking (login and download the package) as soon as
    /// it opens, instead of waiting for the person to press a button. Only makes sense when the PC
    /// is not linked yet and there is a password in memory to link with; a wrong or cancelled prompt
    /// never gets this far, since the window only opens once the prompt itself succeeded.
    /// </summary>
    public static bool ShouldLinkOnOpen(LinkFlowInputs inputs) => !inputs.Linked && inputs.HasPasswordInMemory;

    /// <summary>
    /// Whether Actualizar agente, Volver a vincular and Guardar y vincular can run. They all talk to
    /// the web, and the only credential they have is the password kept from the opening prompt: with
    /// none in memory (should not normally happen, since opening the window always asks first) they
    /// would have nothing to authenticate with.
    /// </summary>
    public static bool WebActionsEnabled(LinkFlowInputs inputs) => inputs.HasPasswordInMemory;

    /// <summary>
    /// "Actualizar agente" and "Volver a vincular" only make sense once this PC is already linked:
    /// there is no agent to update and nothing to link again before that. Not linked, "Vincular esta
    /// PC" in the Estado card is the only way in, so Vinculación offers the quiet address link
    /// instead (see <see cref="ChangeAddressVisible"/>) rather than these two buttons.
    /// </summary>
    public static bool WebActionsVisible(LinkFlowInputs inputs) => inputs.Linked;

    /// <summary>
    /// The quiet "Cambiar la dirección de la aplicación" link in Vinculación: the mirror image of
    /// <see cref="WebActionsVisible"/>, shown only before there is a link to protect from an
    /// accidental address change.
    /// </summary>
    public static bool ChangeAddressVisible(LinkFlowInputs inputs) => !inputs.Linked;
}
