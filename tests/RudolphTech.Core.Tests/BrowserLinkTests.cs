using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary> Whether the "Abrir Rudolph en el navegador" actions may open a browser window. </summary>
public class BrowserLinkTests
{
    [Theory]
    [InlineData("https://rudolph-mvp.vercel.app", "https://rudolph-mvp.vercel.app")]
    [InlineData("http://192.168.0.10:3000", "http://192.168.0.10:3000")]
    [InlineData("https://rudolph-mvp.vercel.app/competencia", "https://rudolph-mvp.vercel.app/competencia")]
    public void ResolvesTheConfiguredAddress(string input, string expected)
    {
        var (url, error) = BrowserLink.Resolve(input);
        Assert.Equal(expected, url);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es una direccion valida")]
    public void RefusesWhenThereIsNothingUsableToOpen(string? input)
    {
        var (url, error) = BrowserLink.Resolve(input);
        Assert.Null(url);
        Assert.Equal(BrowserLink.NotConfigured, error);
    }
}
