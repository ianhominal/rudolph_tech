namespace RudolphTech.Core.Survey;

/// <summary> The last run, as remembered in settings.json and shown in the settings window. </summary>
public sealed class RunSummary
{
    public SurveyRunKind Kind { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public RunOutcomeKind Outcome { get; set; }
    public int Products { get; set; }
    public int Listings { get; set; }
    public string Message { get; set; } = "";

    public static RunSummary From(SurveyRunKind kind, DateTimeOffset startedAt, DateTimeOffset finishedAt, RunOutcome outcome) => new()
    {
        Kind = kind,
        StartedAt = startedAt,
        FinishedAt = finishedAt,
        Outcome = outcome.Kind,
        Products = outcome.Products,
        Listings = outcome.Listings,
        Message = outcome.Message,
    };
}
