namespace RudolphTech.Core.Web;

/// <summary>
/// Decides whether the address configured for the app can be opened in the person's browser.
/// Kept separate from the actual <c>Process.Start</c> call so the decision can be tested without a
/// desktop.
/// </summary>
public static class BrowserLink
{
    public const string NotConfigured = "Primero configurá la dirección de la aplicación.";

    /// <summary> The url to open, or a Spanish message to show instead when there is none. </summary>
    public static (string? Url, string? Error) Resolve(string? configuredUrl) =>
        AppUrl.TryNormalize(configuredUrl, out var url) ? (url, null) : (null, NotConfigured);
}
