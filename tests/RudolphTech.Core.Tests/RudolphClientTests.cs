using System.IO.Compression;
using System.Net;
using System.Text;
using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Logging in exactly like a browser (POST /api/ingresar with the password and next=/, expecting a
/// 303 plus the rudolph_session cookie) and downloading the agent package with that cookie.
/// </summary>
public class RudolphClientTests
{
    private static byte[] SampleZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(zip.CreateEntry("meli-survey.mjs").Open());
            entry.Write("// script");
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task SendsThePasswordAsAFormExactlyLikeTheLoginPage()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.LoginAccepted());
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.LoginAsync("https://x.test", "secreta", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("rudolph_session=un-token-de-sesion", result.SessionCookie);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://x.test/api/ingresar", request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("password=secreta", handler.Bodies[0]);
        Assert.Contains("next=%2F", handler.Bodies[0]);
    }

    [Fact]
    public async Task AWrongPasswordIsReportedInSpanishWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.LoginRejected());
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.LoginAsync("https://x.test", "mala", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.SessionCookie);
        Assert.Contains("contraseña", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADeploymentWithoutASessionSecretIsReportedSeparately()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
            response.Headers.Location = new Uri("https://x.test/ingresar?error=config");
            return response;
        });
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.LoginAsync("https://x.test", "secreta", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("servidor", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ANetworkFailureBecomesAnOfflineMessage()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("no route to host"));
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.LoginAsync("https://x.test", "secreta", CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Offline);
        Assert.Contains("conexión", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnexpectedStatusIsReportedWithItsCode()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.LoginAsync("https://x.test", "secreta", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("502", result.Error);
    }

    [Fact]
    public async Task DownloadsThePackageWithTheSessionCookie()
    {
        var zip = SampleZip();
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        });
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.DownloadPackageAsync("https://x.test", "rudolph_session=abc", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(zip, result.Content);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://x.test/api/agente/descargar", request.RequestUri!.ToString());
        Assert.Equal("rudolph_session=abc", string.Join(";", request.Headers.GetValues("Cookie")));
    }

    [Fact]
    public async Task AnExpiredSessionIsReportedAsSuch()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://x.test/ingresar?next=%2Fapi%2Fagente%2Fdescargar");
            return response;
        });
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.DownloadPackageAsync("https://x.test", "rudolph_session=vieja", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("sesión", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheServerSideErrorMessageIsShownWhenTheRouteSendsOne()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error":"El servidor todavía no tiene configurado SURVEY_INGEST_TOKEN."}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.DownloadPackageAsync("https://x.test", "rudolph_session=abc", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("SURVEY_INGEST_TOKEN", result.Error);
    }

    [Fact]
    public async Task AnEmptyAnswerIsNotTakenForAPackage()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        });
        var client = new RudolphClient(new HttpClient(handler));

        var result = await client.DownloadPackageAsync("https://x.test", "rudolph_session=abc", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ThePasswordNeverEndsUpInTheUrl()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.LoginAccepted());
        var client = new RudolphClient(new HttpClient(handler));

        await client.LoginAsync("https://x.test", "secreta", CancellationToken.None);

        Assert.DoesNotContain("secreta", handler.Requests[0].RequestUri!.ToString());
    }
}
