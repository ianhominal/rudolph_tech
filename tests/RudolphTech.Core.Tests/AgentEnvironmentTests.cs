using RudolphTech.Core.Agent;

namespace RudolphTech.Core.Tests;

/// <summary>
/// The .env that lives next to the survey scripts: how the one downloaded inside the package is
/// read (web/src/lib/agent-package.ts writes it) and how the tray app writes its own copy right
/// before a run (web/scripts/meli-lib.mjs loadEnv reads it).
/// </summary>
public class AgentEnvironmentTests
{
    [Fact]
    public void ReadsTheValuesTheWebAppGenerates()
    {
        var content = string.Join("\n",
            "# Configuracion generada automaticamente al descargar el agente. No compartir este archivo.",
            "RUDOLPH_APP_URL=https://rudolph-mvp.vercel.app",
            "SURVEY_INGEST_TOKEN=un-token-secreto",
            "SURVEY_CHROME_MINIMIZED=",
            "");

        var values = AgentEnvironment.Parse(content);

        Assert.Equal("https://rudolph-mvp.vercel.app", values["RUDOLPH_APP_URL"]);
        Assert.Equal("un-token-secreto", values["SURVEY_INGEST_TOKEN"]);
        Assert.Equal("", values["SURVEY_CHROME_MINIMIZED"]);
    }

    [Fact]
    public void IgnoresBlankLinesCommentsAndSurroundingQuotes()
    {
        var values = AgentEnvironment.Parse("\n# comentario\n  SURVEY_INGEST_TOKEN = \"abc\"  \nbasura sin igual\n");

        Assert.Equal("abc", values["SURVEY_INGEST_TOKEN"]);
        Assert.Single(values);
    }

    [Fact]
    public void WritesTheThreeValuesTheSurveyScriptLooksFor()
    {
        var content = AgentEnvironment.Build("https://rudolph-mvp.vercel.app", "un-token-secreto", chromeOffScreen: true);

        var values = AgentEnvironment.Parse(content);
        Assert.Equal("https://rudolph-mvp.vercel.app", values["RUDOLPH_APP_URL"]);
        Assert.Equal("un-token-secreto", values["SURVEY_INGEST_TOKEN"]);
        Assert.Equal("1", values["SURVEY_CHROME_MINIMIZED"]);
    }

    [Fact]
    public void LeavesTheChromeSettingEmptyWhenItIsOff()
    {
        var values = AgentEnvironment.Parse(AgentEnvironment.Build("https://x.test", "t", chromeOffScreen: false));

        Assert.Equal("", values["SURVEY_CHROME_MINIMIZED"]);
    }

    [Fact]
    public void TheGeneratedFileEndsWithANewLine()
    {
        Assert.EndsWith("\n", AgentEnvironment.Build("https://x.test", "t", chromeOffScreen: false));
    }

    [Fact]
    public void TheProcessEnvironmentCarriesTheSameValues()
    {
        var variables = AgentEnvironment.BuildProcessVariables("https://x.test", "t", chromeOffScreen: true);

        Assert.Equal("https://x.test", variables["RUDOLPH_APP_URL"]);
        Assert.Equal("t", variables["SURVEY_INGEST_TOKEN"]);
        Assert.Equal("1", variables["SURVEY_CHROME_MINIMIZED"]);
    }

    [Fact]
    public void TheProcessEnvironmentOmitsTheChromeSettingWhenItIsOff()
    {
        // An empty string would still count as "set" for some tools; the script treats anything
        // other than "1" as off, but leaving it out entirely is closer to the documented default.
        var variables = AgentEnvironment.BuildProcessVariables("https://x.test", "t", chromeOffScreen: false);

        Assert.False(variables.ContainsKey("SURVEY_CHROME_MINIMIZED"));
    }
}
