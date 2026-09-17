namespace RudolphTech.Core.Web;

/// <summary>
/// The custom URL scheme the installer registers (see installer/RudolphTech.iss), so the web app's
/// "Abrir Rudolph Tech" link can start this program on the office PC when it is closed.
///
/// A web page cannot launch a program by itself, and it should not be able to: a registered scheme is
/// the only door Windows offers, the browser still asks the person to confirm, and it only works when
/// Rudolph Tech is actually installed. That is why the web app shows a download link next to it.
///
/// Windows launches the program with the whole URI as its argument, which is the only reason this
/// exists: Program.cs has to recognise it as "just start normally" instead of taking it for a switch.
/// </summary>
public static class AppProtocol
{
    public const string Scheme = "rudolph";

    /// <summary> What the web app links to. Repeated by hand in SurveyStatus.tsx; AppProtocolTests pins the pair. </summary>
    public const string OpenUri = "rudolph://abrir";

    private const string Prefix = Scheme + ":";

    public static bool IsProtocolArgument(string? argument) =>
        argument is not null && argument.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsProtocolLaunch(IEnumerable<string> args) => args.Any(IsProtocolArgument);
}
