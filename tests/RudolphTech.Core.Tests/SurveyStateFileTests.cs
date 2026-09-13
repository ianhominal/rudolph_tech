using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Reading data/competencia/relevamiento-estado.json, the progress file the survey script writes
/// (see web/scripts/meli-survey.mjs createInitialState/finalize*). The tray app never writes it.
/// </summary>
public class SurveyStateFileTests
{
    private const string FinishedJson = """
    {
      "status": "finished",
      "startedAt": "2026-09-13T09:45:00.000Z",
      "pid": 4242,
      "productIds": ["p1", "p2"],
      "done": 2,
      "current": null,
      "log": "data/competencia/relevamiento.log",
      "results": [
        { "productId": "p1", "ok": true, "count": 6 },
        { "productId": "p2", "ok": true, "count": 4 }
      ],
      "finishedAt": "2026-09-13T10:02:00.000Z"
    }
    """;

    [Fact]
    public void ReadsAFinishedRun()
    {
        var state = SurveyStateFile.Parse(FinishedJson);

        Assert.NotNull(state);
        Assert.Equal(SurveyStatus.Finished, state!.Status);
        Assert.Equal(2, state.Done);
        Assert.Equal(10, state.TotalListings);
        Assert.Equal(2, state.ProductIds.Count);
        Assert.Equal(4242, state.Pid);
        Assert.NotNull(state.FinishedAt);
    }

    [Fact]
    public void ReadsABlockedRun()
    {
        var state = SurveyStateFile.Parse("""
        { "status": "blocked", "startedAt": "2026-09-13T09:45:00.000Z", "done": 1, "productIds": ["p1"], "results": [{ "productId": "p1", "ok": false, "count": 0 }] }
        """);

        Assert.Equal(SurveyStatus.Blocked, state!.Status);
        Assert.Equal(0, state.TotalListings);
    }

    [Fact]
    public void ReadsAnErrorRunWithItsMessage()
    {
        var state = SurveyStateFile.Parse("""
        { "status": "error", "message": "net::ERR_CONNECTION_RESET", "done": 0, "productIds": [], "results": [] }
        """);

        Assert.Equal(SurveyStatus.Error, state!.Status);
        Assert.Equal("net::ERR_CONNECTION_RESET", state.Message);
    }

    [Fact]
    public void ReadsARunStillInProgress()
    {
        var state = SurveyStateFile.Parse("""
        { "status": "running", "startedAt": "2026-09-13T09:45:00.000Z", "done": 0, "current": "p1", "productIds": ["p1"], "results": [] }
        """);

        Assert.Equal(SurveyStatus.Running, state!.Status);
        Assert.Equal("p1", state.Current);
    }

    [Fact]
    public void AnUnknownStatusDoesNotThrow()
    {
        var state = SurveyStateFile.Parse("""{ "status": "algo-nuevo", "done": 0, "productIds": [], "results": [] }""");

        Assert.Equal(SurveyStatus.Unknown, state!.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no json en absoluto")]
    [InlineData("[1,2,3]")]
    public void UnreadableContentGivesNull(string content)
    {
        Assert.Null(SurveyStateFile.Parse(content));
    }

    [Fact]
    public void MissingFieldsFallBackToEmptyValues()
    {
        var state = SurveyStateFile.Parse("{}");

        Assert.NotNull(state);
        Assert.Equal(SurveyStatus.Unknown, state!.Status);
        Assert.Empty(state.ProductIds);
        Assert.Equal(0, state.TotalListings);
    }
}
