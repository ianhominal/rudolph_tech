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

    /// <summary> The app refused because its access period is over, not because anything here is wrong. </summary>
    public bool Expired { get; init; }
}

public sealed class DownloadResult
{
    public bool Success { get; init; }
    public byte[] Content { get; init; } = [];
    public string Error { get; init; } = "";
    public bool Offline { get; init; }

    /// <summary> The app refused because its access period is over, not because anything here is wrong. </summary>
    public bool Expired { get; init; }
}

/// <summary>
/// Rudolph Tech's sign of life, as the web app's POST /api/relevamiento/heartbeat takes it.
///
/// The next run travels as a duration and never as a date, on purpose: the app stamps both dates
/// with its own clock (web/src/lib/agent-presence.ts), so a wrong clock on the office PC cannot
/// leave it looking permanently offline, nor permanently fresh.
/// </summary>
public sealed class Heartbeat
{
    /// <summary> Seconds until the next scheduled survey, or null when none is scheduled (paused, or not linked). </summary>
    public int? NextRunInSeconds { get; init; }

    public bool Paused { get; init; }

    public bool Running { get; init; }

    /// <summary>
    /// Whether the periodic check for surveys requested from the web is scheduled at all. Off means a
    /// survey asked for there is never picked up, however connected and unpaused this program looks,
    /// which is the one case where a reassuring line on the web would be worse than the stuck one it
    /// replaced.
    /// </summary>
    public bool ServesRequests { get; init; }

    /// <summary> Past this the office PC is telling a story about its own clock, and the web app refuses it. </summary>
    public const int MaximumNextRunSeconds = 31 * 24 * 60 * 60;

    /// <summary>
    /// Builds one from a scheduled instant, clamped into exactly the range the web app accepts
    /// (web/src/lib/agent-presence.ts): an overdue survey reports zero seconds, which reads there as
    /// "arranca en un momento", and nothing can turn into a rejected request.
    /// </summary>
    public static Heartbeat For(DateTimeOffset? nextRunAt, DateTimeOffset now, bool paused, bool running, bool servesRequests)
    {
        int? seconds = null;
        if (nextRunAt is { } next)
        {
            var remaining = (next - now).TotalSeconds;
            seconds = remaining <= 0 ? 0 : (int)Math.Min(MaximumNextRunSeconds, Math.Round(remaining));
        }
        return new Heartbeat { NextRunInSeconds = seconds, Paused = paused, Running = running, ServesRequests = servesRequests };
    }
}

/// <summary>
/// Talks to the deployed web app the same way a browser does, because that is what its two
/// browser-shaped endpoints expect.
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
            var expired = await ReadExpiredAsync(response, cancellation);
            if (expired is not null)
            {
                return new LoginResult { Error = expired, Expired = true };
            }

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
            var expired = await ReadExpiredAsync(response, cancellation);
            if (expired is not null)
            {
                return new DownloadResult { Error = expired, Expired = true };
            }

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

    /// <summary>
    /// Tells the web app the program is open and when it will run next. Fires on a timer long after
    /// whoever typed the password closed the settings window, so it carries the stored ingest token
    /// rather than the session cookie, exactly like the survey's own uploads do.
    ///
    /// Never throws and never disturbs anything: a PC with no internet just misses a few heartbeats
    /// and the web app says the program is closed until they come back. Returns whether it landed,
    /// only so the caller can decide about writing one line in the log.
    /// </summary>
    public async Task<bool> PostHeartbeatAsync(string appUrl, string ingestToken, Heartbeat heartbeat, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AppUrl.HeartbeatEndpoint(appUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(heartbeat, HeartbeatJson), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ingestToken);

        try
        {
            using var response = await _http.SendAsync(request, cancellation);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions HeartbeatJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The trial gate's answer, or null when this response is not that.
    ///
    /// The web app refuses every request with 403 and { "error": "...", "expired": true }
    /// once its access period is over (web/src/proxy.ts). That is a different situation from a
    /// wrong password or a broken install — nothing here is misconfigured and retrying will not
    /// help — so every caller reports it in its own words instead of as a status code.
    /// </summary>
    private static async Task<string?> ReadExpiredAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden) return null;

        var body = await SafeReadAsync(response, cancellation);
        if (!IsExpiredBody(body)) return null;

        return ReadJsonError(body) ?? "El acceso a Rudolph venció. Avisá a quien administra la aplicación.";
    }

    /// <summary> Pure: whether a JSON body carries the gate's expired:true flag. </summary>
    internal static bool IsExpiredBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("expired", out var expired)
                && expired.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
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
