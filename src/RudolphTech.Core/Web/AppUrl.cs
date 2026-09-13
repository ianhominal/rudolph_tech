using System.Diagnostics.CodeAnalysis;

namespace RudolphTech.Core.Web;

/// <summary> The address of the web app, as typed by a person and as used by the client. </summary>
public static class AppUrl
{
    public const string LoginPath = "/api/ingresar";
    public const string PackagePath = "/api/agente/descargar";

    /// <summary> Adds https:// when it is missing and drops trailing slashes. Throws on anything unusable. </summary>
    public static string Normalize(string input) =>
        TryNormalize(input, out var normalized) ? normalized : throw new FormatException($"Dirección inválida: {input}");

    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim();
        if (text.Any(char.IsWhiteSpace)) return false;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;

        normalized = text.TrimEnd('/');
        return true;
    }

    public static string LoginEndpoint(string appUrl) => appUrl.TrimEnd('/') + LoginPath;

    public static string PackageEndpoint(string appUrl) => appUrl.TrimEnd('/') + PackagePath;
}
