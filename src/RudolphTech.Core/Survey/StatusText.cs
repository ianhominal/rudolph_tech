using RudolphTech.Core.Settings;

namespace RudolphTech.Core.Survey;

/// <summary>
/// Every line the person reads in the tray and in the settings window. Spanish rioplatense, no
/// dashes, and dates always shown exactly as they were recorded (never converted to another time
/// zone, which on this PC is the office's own clock anyway).
/// </summary>
public static class StatusText
{
    private const string DateAndTime = "dd/MM/yyyy HH:mm";
    private const string Time = "HH:mm";

    public static string PackageStatus(DateTimeOffset? lastDownload, bool agentInstalled)
    {
        if (lastDownload is null || !agentInstalled) return "El agente todavía no se descargó.";
        return $"Agente descargado el {lastDownload.Value.ToString(DateAndTime)}.";
    }

    public static string Session(bool loggedIn, string? appUrl) => loggedIn
        ? $"Sesión iniciada en {appUrl}."
        : "Sin sesión. Escribí la contraseña y tocá Iniciar sesión.";

    /// <summary> The quiet line at the bottom of the settings window, under Vinculación con la aplicación. </summary>
    public static string LinkSummary(string appUrl, DateTimeOffset? lastPackageDownload)
    {
        var package = lastPackageDownload is { } downloaded
            ? $"Agente actualizado el {downloaded.ToString(DateAndTime)}."
            : "El agente todavía no se descargó.";
        return $"Vinculada con {appUrl}. {package}";
    }

    public static string LastRun(RunSummary? summary)
    {
        if (summary is null) return "Todavía no se hizo ningún relevamiento.";

        var finished = summary.FinishedAt is { } end ? end.ToString(Time) : "en curso";
        var what = summary.Outcome switch
        {
            RunOutcomeKind.Finished => $"{summary.Listings} publicaciones de {summary.Products} producto(s)",
            RunOutcomeKind.Blocked => "detenido, Mercado Libre pidió una verificación",
            RunOutcomeKind.Interrupted => "interrumpido antes de terminar",
            RunOutcomeKind.Nothing => "sin novedades",
            _ => "con errores",
        };

        return $"{Describe(summary.Kind)} del {summary.StartedAt.ToString(DateAndTime)} a las {finished}: {what}.";
    }

    /// <summary>
    /// Shown in the Pausar/Reanudar balloon when unpausing. A hold in effect (BB-2) means the plain
    /// "cada N minutos" claim is false until it lifts (found in the independent review of A1: it kept
    /// saying that through an active hold), so it gets replaced with the same resume clause the blocked
    /// balloon uses (T3/T5) rather than inventing a new sentence for a case the texts table never named.
    ///
    /// resumesAt is the effective next automatic run (AgentService.NextRunAt, i.e.
    /// ScheduleDecider.NextRunAt, computed by the caller so this stays a pure formatter): the raw
    /// settings.BlockedUntil alone can name a time later than what will really happen, because BB-4
    /// makes the daily survey immune to a hold (H-1, found in the second independent review: a hold
    /// that outlasted the next daily time still claimed "se reanudan" at the hold's own end, while the
    /// daily survey ran, unheld, before it). Still gated on settings.BlockedUntil, which only answers
    /// whether a hold exists at all; resumesAt only replaces the number named once that gate already
    /// says yes, and falls back to the raw hold if a caller has none to offer (every real caller does).
    /// </summary>
    public static string Schedule(AppSettings settings, bool paused, DateTimeOffset now, DateTimeOffset? resumesAt)
    {
        if (paused) return "Todo en pausa. Ningún relevamiento automático va a arrancar.";
        if (settings.BlockedUntil is { } until && until > now) return $"Los relevamientos automáticos se reanudan {ResumeAt(resumesAt ?? until, now)}.";
        return $"Atiende pedidos cada {settings.PendingIntervalMinutes} minutos y releva todo a las {settings.DailyTime.ToString(Time)}.";
    }

    /// <summary>
    /// Windows truncates a notify icon tooltip past 63 characters, so every branch stays short on
    /// purpose (checked in StatusTextTests). Order matters: waiting is a live sub-state of running and
    /// has to win over it; paused wins over a hold, because pausing is a person's own deliberate choice
    /// and a stale hold underneath it is not news.
    ///
    /// resumesAt: see Schedule's own doc comment, same contract and same reason (H-1). Its horizon
    /// safety is also why the held branch below needs no separate MaximumHoldHorizon check of its own
    /// (M-2, found in the same review): resumesAt comes from ScheduleDecider.NextRunAt, whose pending
    /// leg already ignores a settings.BlockedUntil farther out than that horizon and falls back to the
    /// ordinary cadence (pinned in ScheduleDeciderTests), so a corrupted far-future BlockedUntil can
    /// still pass the gate below but can never make this branch name a bogus far date.
    /// </summary>
    public static string TrayTooltip(AppSettings settings, bool paused, bool running, DateTimeOffset now, DateTimeOffset? resumesAt, bool waiting = false)
    {
        if (waiting) return "Rudolph Tech: hay que verificar en la ventana de Chrome";
        if (running) return "Rudolph Tech: relevando ahora";
        if (paused) return "Rudolph Tech: en pausa";
        if (settings.BlockedUntil is { } until && until > now)
        {
            var held = $"Rudolph Tech: sin relevar hasta {HoldClause(resumesAt ?? until, now)}";
            return held.Length <= 63 ? held : held[..63];
        }
        var text = $"Rudolph Tech: diario {settings.DailyTime.ToString(Time)}, pedidos {settings.PendingIntervalMinutes} min";
        return text.Length <= 63 ? text : text[..63];
    }

    /// <summary> "a las 16:40", "mañana a las 06:45", or "el 24/09 a las 06:45". Used by the blocked balloon (T3, T5) and, in its short form (see HoldClause), by the tray tooltip (T4). </summary>
    public static string ResumeAt(DateTimeOffset until, DateTimeOffset now) => RelativeDay(until, now) switch
    {
        DayRelation.Today => $"a las {until.ToString(Time)}",
        DayRelation.Tomorrow => $"mañana a las {until.ToString(Time)}",
        _ => $"el {until:dd/MM} a las {until.ToString(Time)}",
    };

    /// <summary> The tooltip's shorter phrasing after "sin relevar hasta ": "las 16:40", "mañana 06:45", or "el 24/09 06:45". </summary>
    private static string HoldClause(DateTimeOffset until, DateTimeOffset now) => RelativeDay(until, now) switch
    {
        DayRelation.Today => $"las {until.ToString(Time)}",
        DayRelation.Tomorrow => $"mañana {until.ToString(Time)}",
        _ => $"el {until:dd/MM} {until.ToString(Time)}",
    };

    private enum DayRelation { Today, Tomorrow, Later }

    private static DayRelation RelativeDay(DateTimeOffset until, DateTimeOffset now)
    {
        var untilDay = DateOnly.FromDateTime(until.Date);
        var today = DateOnly.FromDateTime(now.Date);
        if (untilDay == today) return DayRelation.Today;
        return untilDay == today.AddDays(1) ? DayRelation.Tomorrow : DayRelation.Later;
    }

    private static string Describe(SurveyRunKind kind) => kind switch
    {
        SurveyRunKind.Pending => "Pedido atendido",
        SurveyRunKind.Daily => "Relevamiento diario",
        _ => "Relevamiento manual",
    };
}
