namespace RudolphTech.Core.Agent;

/// <summary>
/// When to download the agent package again. Once a day, right before the daily survey, so the
/// scripts on the office PC follow whatever was deployed to the web app, plus whenever the scripts
/// are simply not there.
/// </summary>
public static class PackageRefreshPolicy
{
    public static bool NeedsRefresh(DateTimeOffset? lastDownload, DateTimeOffset now, bool agentInstalled)
    {
        if (!agentInstalled) return true;
        if (lastDownload is not { } last) return true;

        // A date in the future means the machine clock moved; treat it as up to date rather than
        // downloading on every single check.
        return last.Date < now.Date;
    }
}
