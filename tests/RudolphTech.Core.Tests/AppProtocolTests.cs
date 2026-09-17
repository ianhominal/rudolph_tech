using RudolphTech.Core.Web;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The custom scheme the installer registers so the web app's "Abrir Rudolph Tech" link can start
/// this program. Windows hands the whole URI over as the process's only argument, so what matters is
/// recognising it as "just start normally" rather than mistaking it for a switch.
/// </summary>
public class AppProtocolTests
{
    [Theory]
    [InlineData("rudolph://abrir")]
    [InlineData("rudolph://abrir/")]
    [InlineData("RUDOLPH://Abrir")]
    [InlineData("rudolph:abrir")]
    public void RecognisesTheSchemeHoweverWindowsSpellsItBack(string argument)
    {
        Assert.True(AppProtocol.IsProtocolArgument(argument));
    }

    [Theory]
    [InlineData("--startup")]
    [InlineData("--salir")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://rudolph-mvp.vercel.app")]
    public void LeavesEverythingElseAlone(string? argument)
    {
        Assert.False(AppProtocol.IsProtocolArgument(argument));
    }

    [Fact]
    public void DoesNotMatchASchemeThatMerelyStartsTheSameWay()
    {
        // "rudolphtech://" is somebody else's scheme, not ours.
        Assert.False(AppProtocol.IsProtocolArgument("rudolphtech://abrir"));
    }

    [Fact]
    public void FindsTheUriAmongWhateverElseWindowsPassed()
    {
        Assert.True(AppProtocol.IsProtocolLaunch(["--startup", "rudolph://abrir"]));
        Assert.False(AppProtocol.IsProtocolLaunch(["--startup"]));
        Assert.False(AppProtocol.IsProtocolLaunch([]));
    }

    [Fact]
    public void TheLinkTheWebAppShowsIsTheOneThisRecognises()
    {
        // web/src/app/competencia/SurveyStatus.tsx hardcodes this same string in its "Abrir Rudolph Tech"
        // link. If one of the two ever changes, the link silently stops working, so pin it here.
        Assert.Equal("rudolph://abrir", AppProtocol.OpenUri);
        Assert.True(AppProtocol.IsProtocolArgument(AppProtocol.OpenUri));
    }
}
