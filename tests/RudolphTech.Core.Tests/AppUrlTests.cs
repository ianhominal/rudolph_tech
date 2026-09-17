using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary> Whatever the person types into the "Dirección de la aplicación" box has to become a usable base url. </summary>
public class AppUrlTests
{
    [Theory]
    [InlineData("https://rudolph-mvp.vercel.app", "https://rudolph-mvp.vercel.app")]
    [InlineData("https://rudolph-mvp.vercel.app/", "https://rudolph-mvp.vercel.app")]
    [InlineData("  https://rudolph-mvp.vercel.app///  ", "https://rudolph-mvp.vercel.app")]
    [InlineData("rudolph-mvp.vercel.app", "https://rudolph-mvp.vercel.app")]
    [InlineData("http://192.168.0.10:3000", "http://192.168.0.10:3000")]
    [InlineData("https://rudolph-mvp.vercel.app/competencia", "https://rudolph-mvp.vercel.app/competencia")]
    public void NormalizesWhatThePersonTyped(string input, string expected)
    {
        Assert.Equal(expected, AppUrl.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es una direccion valida")]
    [InlineData("ftp://algo")]
    public void RejectsWhatIsNotAWebAddress(string input)
    {
        Assert.False(AppUrl.TryNormalize(input, out _));
    }

    [Fact]
    public void BuildsTheEndpointsTheAppTalksTo()
    {
        Assert.Equal("https://x.test/api/ingresar", AppUrl.LoginEndpoint("https://x.test"));
        Assert.Equal("https://x.test/api/agente/descargar", AppUrl.PackageEndpoint("https://x.test"));
    }

    [Fact]
    public void BuildsTheHeartbeatEndpointWithoutDoublingTheSlash()
    {
        Assert.Equal("https://x.test/api/relevamiento/heartbeat", AppUrl.HeartbeatEndpoint("https://x.test"));
        Assert.Equal("https://x.test/api/relevamiento/heartbeat", AppUrl.HeartbeatEndpoint("https://x.test/"));
    }
}
