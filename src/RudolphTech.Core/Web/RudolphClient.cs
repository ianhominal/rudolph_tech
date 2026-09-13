using System.Net;
using System.Text.Json;

namespace RudolphTech.Core.Web;

public sealed class LoginResult
{
    public bool Success { get; init; }

    /// <summary> Ready to send as a Cookie header, for example "rudolph_session=abc". Never stored on disk. </summary>
    public string? SessionCookie { get; init; }

    public string Error { get; init; } = "";

    /// <summary> The app could not be reached at all, as opposed to answering that the password is wrong. </summary>
    public bool Offline { get; init; }
}

public sealed class DownloadResult
{
    public bool Success { get; init; }
    public byte[] Content { get; init; } = [];
    public string Error { get; init; } = "";
    public bool Offline { get; init; }
}

/// <summary>
/// Talks to the deployed web app the same way a browser does, because that is what its two
/// endpoints expect.
///
/// POST /api/ingresar takes a form with "password" and "next", answers 303 and sets the
/// rudolph_session cookie (web/src/app/api/ingresar/route.ts). A wrong password is also a 303, back
/// to /ingresar?error=1, which is why redirects must not be followed automatically.
/// GET /api/agente/descargar answers with the zip when that cookie travels along
/// (web/src/app/api/agente/descargar/route.ts).
///
/// The HttpClient is injected so every branch here is covered by tests with a fake handler; use
/// <see cref="CreateHttpClient"/> for the real one, which is configured exactly as this needs.
/// </summary>
public sealed class RudolphClient
{
    private const string SessionCookieName = "rudolph_session";

    private readonly HttpClient _http;

    public RudolphClient(HttpClient http) => _http = http;

    /// <summary> Redirects must not be followed (the 303 is the answer) and cookies are handled by hand. </summary>
    public static HttpClient CreateHttpClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

    public async Task<LoginResult> LoginAsync(string appUrl, string password, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AppUrl.LoginEndpoint(appUrl))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = password, ["next"] = "/" }),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellation);
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            return new LoginResult { Error = OfflineMessage(appUrl), Offline = true };
        }

        using (response)
        {
            if (!IsRedirect(response.StatusCode))
            {
                return new LoginResult { Error = $"La aplicación respondió {(int)response.StatusCode} al iniciar sesión." };
            }

            var location = response.Headers.Location?.ToString() ?? "";
            if (location.Contains("error=config", StringComparison.OrdinalIgnoreCase))
            {
                return new LoginResult { Error = "El servidor no tiene bien configurada la sesión. Avisá a quien administra la aplicación." };
            }

            if (location.Contains("/ingresar", StringComparison.OrdinalIgnoreCase))
            {
                return new LoginResult { Error = "La contraseña no es correcta." };
            }

            var cookie = ReadSessionCookie(response);
            if (cookie is null)
            {
                return new LoginResult { Error = "La aplicación no devolvió la cookie de sesión." };
            }

            return new LoginResult { Success = true, SessionCookie = cookie };
        }
    }

    public async Task<DownloadResult> DownloadPackageAsync(string appUrl, string sessionCookie, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppUrl.PackageEndpoint(appUrl));
        request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellation);
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            return new DownloadResult { Error = OfflineMessage(appUrl), Offline = true };
        }

        using (response)
        {
            if (IsRedirect(response.StatusCode))
            {
                return new DownloadResult { Error = "La sesión venció. Iniciá sesión de nuevo." };
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(response, cancellation);
                var detail = ReadJsonError(body);
                return new DownloadResult
                {
                    Error = detail is null
                        ? $"La aplicación respondió {(int)response.StatusCode} al descargar el agente."
                        : $"La aplicación no pudo entregar el agente: {detail}",
                };
            }

            var content = await response.Content.ReadAsByteArrayAsync(cancellation);
            if (content.Length == 0)
            {
                return new DownloadResult { Error = "La aplicación devolvió un paquete vacío." };
            }

            return new DownloadResult { Success = true, Content = content };
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.MovedPermanently
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsNetworkFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static string OfflineMessage(string appUrl) => $"Sin conexión con {appUrl}. Revisá internet y volvé a intentar.";

    private static string? ReadSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var first = cookie.Split(';')[0].Trim();
            if (first.StartsWith(SessionCookieName + "=", StringComparison.Ordinal) && first.Length > SessionCookieName.Length + 1)
            {
                return first;
            }
        }
        return null;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellation);
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            return "";
        }
    }

    /// <summary> Both routes answer errors as { "error": "..." } in Spanish; show that text as it is. </summary>
    private static string? ReadJsonError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
