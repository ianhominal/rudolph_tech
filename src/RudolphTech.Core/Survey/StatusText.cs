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

    public static string Schedule(AppSettings settings, bool paused)
    {
        if (paused) return "Todo en pausa. Ningún relevamiento automático va a arrancar.";
        return $"Atiende pedidos cada {settings.PendingIntervalMinutes} minutos y releva todo a las {settings.DailyTime.ToString(Time)}.";
    }

    /// <summary> Windows truncates a notify icon tooltip past 63 characters, so this stays short on purpose. </summary>
    public static string TrayTooltip(AppSettings settings, bool paused, bool running)
    {
        if (running) return "Rudolph Tech: relevando ahora";
        if (paused) return "Rudolph Tech: en pausa";
        var text = $"Rudolph Tech: diario {settings.DailyTime.ToString(Time)}, pedidos {settings.PendingIntervalMinutes} min";
        return text.Length <= 63 ? text : text[..63];
    }

    private static string Describe(SurveyRunKind kind) => kind switch
    {
        SurveyRunKind.Pending => "Pedido atendido",
        SurveyRunKind.Daily => "Relevamiento diario",
        _ => "Relevamiento manual",
    };
}
