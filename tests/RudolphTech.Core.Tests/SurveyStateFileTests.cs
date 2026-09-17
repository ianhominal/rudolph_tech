using RudolphTech.Core.Survey;

namespace RudolphTech.Core.Tests;

/// <summary>
/// Reading data/competencia/relevamiento-estado.json, the progress file the survey script writes
/// (see web/scripts/meli-survey.mjs createInitialState/finalize*). The tray app never writes it.
///
/// Every fixture below is the shape the script really writes. That has to be said out loud, because
/// these tests used to invent it: they fed `{ "productId": ..., "count": ... }`, which no file has
/// ever contained, and the parser read the same invented names, so both agreed and production
/// reported "Se relevaron 0 publicaciones" after surveying 222 of them. A fixture copied from a real
/// run is the only kind worth having here.
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
        { "id": "p1", "ok": true, "listings": 6 },
        { "id": "p2", "ok": true, "listings": 4 }
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
    public void ReadsTheProductIdsOfEachResult()
    {
        var state = SurveyStateFile.Parse(FinishedJson);
        Assert.Equal(["p1", "p2"], state!.Results.Select(r => r.ProductId));
    }

    [Fact]
    public void ReadsTheProductBeingSurveyedRightNow()
    {
        // `current` is an object while a run is in progress, `{ id, name }`, and null the rest of the time.
        // Reading it as a string left it silently null even mid run.
        var state = SurveyStateFile.Parse("""
        { "status": "running", "startedAt": "2026-09-13T09:45:00.000Z", "done": 1, "current": { "id": "zorra", "name": "Zorra hidráulica" }, "productIds": ["zorra"], "results": [] }
        """);
        Assert.Equal("Zorra hidráulica", state!.CurrentProductName);
    }

    [Fact]
    public void ReadsARealRunsOwnFileWithoutInventingAnything()
    {
        // Trimmed from the run of 17 September 2026: 36 products, 222 listings, which the tray reported
        // as 0. Field for field as the script wrote it.
        var state = SurveyStateFile.Parse("""
        {
          "status": "finished",
          "startedAt": "2026-09-17T17:57:24.000Z",
          "pid": 12924,
          "productIds": ["teclado-y-mouse-recargable-negro", "capybara-palito"],
          "done": 2,
          "current": null,
          "log": "data/competencia/relevamiento.log",
          "results": [
            { "id": "teclado-y-mouse-recargable-negro", "ok": true, "listings": 6 },
            { "id": "capybara-palito", "ok": true, "listings": 6 }
          ],
          "finishedAt": "2026-09-17T19:27:07.000Z"
        }
        """);

        Assert.Equal(12, state!.TotalListings);
        Assert.Equal("teclado-y-mouse-recargable-negro", state.Results[0].ProductId);
        Assert.True(state.Results[0].Ok);
    }

    [Fact]
    public void ReadsABlockedRun()
    {
        var state = SurveyStateFile.Parse("""
        { "status": "blocked", "startedAt": "2026-09-13T09:45:00.000Z", "done": 1, "productIds": ["p1"], "results": [{ "id": "p1", "ok": false, "listings": 0 }] }
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
        { "status": "running", "startedAt": "2026-09-13T09:45:00.000Z", "done": 0, "current": { "id": "p1", "name": "Zorra hidráulica" }, "productIds": ["p1"], "results": [] }
        """);

        Assert.Equal(SurveyStatus.Running, state!.Status);
        Assert.Equal("Zorra hidráulica", state.CurrentProductName);
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
