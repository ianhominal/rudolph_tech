using System.Net;

namespace RudolphTech.Core.Tests;

/// <summary> Records every request and answers with whatever the test queued, so the client can be exercised offline. </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(request);
        Bodies.Add(body);
        return _responder(request, body);
    }

    /// <summary> The 303 with a Set-Cookie header that POST /api/ingresar answers with for the right password. </summary>
    public static HttpResponseMessage LoginAccepted(string cookieValue = "un-token-de-sesion")
    {
        var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
        response.Headers.Location = new Uri("https://x.test/");
        response.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            $"rudolph_session={cookieValue}; Path=/; HttpOnly; SameSite=Lax; Secure; Max-Age=2592000");
        return response;
    }

    /// <summary> The 303 back to /ingresar?error=1 the route answers with for a wrong password. </summary>
    public static HttpResponseMessage LoginRejected()
    {
        var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
        response.Headers.Location = new Uri("https://x.test/ingresar?error=1&next=%2F");
        return response;
    }
}
