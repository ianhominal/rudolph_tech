namespace RudolphTech.Core.Survey;

/// <summary> Why a survey was started. </summary>
public enum SurveyRunKind
{
    /// <summary> Scheduled "--pending" check: silent when there was nothing to do. </summary>
    Pending,

    /// <summary> Scheduled "--all" survey. </summary>
    Daily,

    /// <summary> Somebody pressed "Relevar ahora" and is waiting for an answer. </summary>
    Manual,
}

public enum RunOutcomeKind
{
    Finished,

    /// <summary> The run had nothing to do (no pending request, no product with search terms). </summary>
    Nothing,

    /// <summary> Mercado Libre asked for a verification and the run stopped on purpose. </summary>
    Blocked,

    Interrupted,
    Error,
}

/// <summary>
/// Turns the survey script's exit code plus its progress file into the one line the tray balloon
/// shows. Exit codes come from web/scripts/meli-survey.mjs: 0 finished, 2 blocked, 3 nothing was
/// captured, anything else an error. The progress file wins when the two disagree, because it is
/// written by the run itself and says more.
/// </summary>
public sealed class RunOutcome
{
    public required RunOutcomeKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public int Products { get; init; }
    public int Listings { get; init; }

    /// <summary> A silent scheduled check that found nothing pending must not pop a balloon every 15 minutes. </summary>
    public bool ShouldNotify => Kind != RunOutcomeKind.Nothing || _notifyOnNothing;

    private readonly bool _notifyOnNothing;

    private RunOutcome(bool notifyOnNothing) => _notifyOnNothing = notifyOnNothing;

    public static RunOutcome From(SurveyRunKind kind, int exitCode, SurveyState? state, DateTimeOffset? resumesAt = null)
    {
        var products = state?.Done ?? 0;
        var listings = state?.TotalListings ?? 0;
        var manual = kind != SurveyRunKind.Pending;

        var resolved = Resolve(exitCode, state);
        return resolved switch
        {
            RunOutcomeKind.Blocked => new RunOutcome(manual)
            {
                Kind = RunOutcomeKind.Blocked,
                Title = "Relevamiento detenido",
                Message = BlockedMessage(state?.BlockedReason, resumesAt),
                Products = products,
                Listings = listings,
            },
            RunOutcomeKind.Interrupted => new RunOutcome(manual)
            {
                Kind = RunOutcomeKind.Interrupted,
                Title = "Relevamiento interrumpido",
                Message = "El relevamiento se interrumpió antes de terminar.",
                Products = products,
                Listings = listings,
            },
            RunOutcomeKind.Nothing => new RunOutcome(manual)
            {
                Kind = RunOutcomeKind.Nothing,
                Title = "Sin novedades",
                Message = kind == SurveyRunKind.Pending
                    ? "No había ningún pedido de relevamiento pendiente."
                    : "No se pudo capturar ninguna publicación esta vez.",
            },
            RunOutcomeKind.Finished => new RunOutcome(manual)
            {
                Kind = RunOutcomeKind.Finished,
                Title = "Relevamiento terminado",
                Message = $"Se relevaron {listings} publicaciones de {products} producto(s).",
                Products = products,
                Listings = listings,
            },
            _ => new RunOutcome(manual)
            {
                Kind = RunOutcomeKind.Error,
                Title = "El relevamiento falló",
                Message = string.IsNullOrWhiteSpace(state?.Message)
                    ? $"El relevamiento terminó con un error (código {exitCode}). Mirá el registro para ver el detalle."
                    : $"El relevamiento terminó con un error: {state!.Message}",
                Products = products,
                Listings = listings,
            },
        };
    }

    /// <summary>
    /// Which kind of outcome this is: the state file first (it is written by the run itself and says
    /// more), the exit code otherwise. Public so AgentService can ask the same question before
    /// RunOutcome.From runs, which is what lets it compute the hold (and so resumesAt) before the
    /// message exists instead of after; see AgentService's own ResumesAt helper.
    /// </summary>
    public static RunOutcomeKind Resolve(int exitCode, SurveyState? state)
    {
        switch (state?.Status)
        {
            case SurveyStatus.Blocked:
                return RunOutcomeKind.Blocked;
            case SurveyStatus.Interrupted:
                return RunOutcomeKind.Interrupted;
            case SurveyStatus.Error:
                return RunOutcomeKind.Error;
        }

        return exitCode switch
        {
            0 => RunOutcomeKind.Finished,
            2 => RunOutcomeKind.Blocked,
            3 => RunOutcomeKind.Nothing,
            _ => RunOutcomeKind.Error,
        };
    }

    /// <summary>
    /// T3 (timed out), T6 (the per run wall cap) and T9 (a person closed the Chrome window) all mean
    /// the same thing happened: nobody finished answering, so they share one sentence, exactly as the
    /// texts table says "same as T3" for T6 and T9. T5 is its own sentence: the window itself could not
    /// be shown, so nobody even had the chance. No reason, or one this build does not recognise (an
    /// older script never sends one), falls back to a sentence that is still true whatever wrote the
    /// state. resumesAt null, meaning nobody has a hold to name yet, drops the resume clause entirely:
    /// a surface that does not know the retry time must not invent one.
    /// </summary>
    private static string BlockedMessage(string? reason, DateTimeOffset? resumesAt)
    {
        var body = reason switch
        {
            "verification-no-window" =>
                "Mercado Libre pidió una verificación y la ventana de Chrome no se pudo mostrar en esta pantalla, así que nadie pudo completarla.",
            "verification-timeout" or "verification-cap" or "verification-window-closed" =>
                "La verificación de Mercado Libre quedó sin completar y el relevamiento se detuvo.",
            _ =>
                "Mercado Libre pidió una verificación y el relevamiento se detuvo.",
        };
        return resumesAt is { } until
            ? $"{body} Los relevamientos automáticos se reanudan {StatusText.ResumeAt(until, DateTimeOffset.Now)}."
            : body;
    }
}
