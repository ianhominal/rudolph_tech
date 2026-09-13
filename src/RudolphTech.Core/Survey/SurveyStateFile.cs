using System.Text.Json;

namespace RudolphTech.Core.Survey;

/// <summary> The statuses web/scripts/meli-survey.mjs writes into its progress file. </summary>
public enum SurveyStatus
{
    Unknown,
    Running,
    Finished,
    Blocked,
    Error,

    /// <summary> Written by nobody: the web app derives it from a "running" state whose process is gone. </summary>
    Interrupted,
}

/// <summary> One product's line inside the progress file. </summary>
public sealed class SurveyResult
{
    public string ProductId { get; init; } = "";
    public bool Ok { get; init; }
    public int Count { get; init; }
}

/// <summary> The progress file as the tray app reads it. Written only by the survey script. </summary>
public sealed class SurveyState
{
    public SurveyStatus Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int Pid { get; init; }
    public IReadOnlyList<string> ProductIds { get; init; } = [];
    public int Done { get; init; }
    public string? Current { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<SurveyResult> Results { get; init; } = [];

    /// <summary> Total listings read across every product of the run. </summary>
    public int TotalListings { get; init; }
}

/// <summary>
/// Reads data/competencia/relevamiento-estado.json out of the agent folder. Deliberately forgiving:
/// the file is written by another process and can be half written exactly while this reads it, and
/// a survey that ended badly is still better news than an exception in the tray.
/// </summary>
public static class SurveyStateFile
{
    public static SurveyState? Read(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SurveyState? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var results = ReadResults(root);
            return new SurveyState
            {
                Status = ParseStatus(ReadString(root, "status")),
                StartedAt = ReadDate(root, "startedAt"),
                FinishedAt = ReadDate(root, "finishedAt"),
                Pid = root.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var pidValue) ? pidValue : 0,
                ProductIds = ReadStringArray(root, "productIds"),
                Done = root.TryGetProperty("done", out var done) && done.TryGetInt32(out var doneValue) ? doneValue : 0,
                Current = ReadString(root, "current"),
                Message = ReadString(root, "message"),
                Results = results,
                TotalListings = results.Sum(result => result.Count),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SurveyStatus ParseStatus(string? status) => status switch
    {
        "running" => SurveyStatus.Running,
        "finished" => SurveyStatus.Finished,
        "blocked" => SurveyStatus.Blocked,
        "error" => SurveyStatus.Error,
        "interrupted" => SurveyStatus.Interrupted,
        _ => SurveyStatus.Unknown,
    };

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        var items = new List<string>();
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return items;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text) items.Add(text);
        }
        return items;
    }

    private static List<SurveyResult> ReadResults(JsonElement root)
    {
        var results = new List<SurveyResult>();
        if (!root.TryGetProperty("results", out var array) || array.ValueKind != JsonValueKind.Array) return results;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            results.Add(new SurveyResult
            {
                ProductId = ReadString(item, "productId") ?? "",
                Ok = item.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
                Count = item.TryGetProperty("count", out var count) && count.TryGetInt32(out var countValue) ? countValue : 0,
            });
        }
        return results;
    }
}
